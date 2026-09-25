using UPBazaar.Modules.Shipping.Application;
using UPBazaar.Modules.Shipping.Contracts.Events;
using UPBazaar.Modules.Shipping.Domain;

namespace UPBazaar.UnitTests.Shipping;

/// <summary>
/// Cash on delivery: what the courier owes for a delivered parcel, what its remittances pay, and
/// reading its remittance report.
/// </summary>
public sealed class CodTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Cash_is_in_once_paid_in_full_and_says_so_once()
    {
        var receivable = Receivable(417m);

        receivable.Pay(400m, Now);
        receivable.Status.ShouldBe(CodStatus.ShortPaid);
        receivable.IsCashIn.ShouldBeFalse();
        receivable.DomainEvents.ShouldBeEmpty();

        receivable.Pay(17m, Now);
        receivable.Status.ShouldBe(CodStatus.Received);
        receivable.CashInAtUtc.ShouldBe(Now);

        receivable.Pay(5m, Now);
        receivable.Status.ShouldBe(CodStatus.Over);
        receivable.DomainEvents.OfType<CodCashReceivedDomainEvent>().Count().ShouldBe(1);
    }

    [Fact]
    public void Writing_off_counts_the_cash_as_in_and_only_while_something_is_owed()
    {
        var receivable = Receivable(417m);
        receivable.Pay(400m, Now);

        receivable.WriteOff("Courier lost the difference.", "finance", Now).IsSuccess.ShouldBeTrue();

        receivable.Status.ShouldBe(CodStatus.WrittenOff);
        receivable.IsCashIn.ShouldBeTrue();
        receivable.DomainEvents.OfType<CodCashReceivedDomainEvent>().ShouldHaveSingleItem();
        receivable.WriteOff("Again.", "finance", Now).Error.ShouldBe(ShippingErrors.CodNothingOwed);
    }

    [Fact]
    public void Overdue_after_the_cycle_while_still_owed()
    {
        var receivable = Receivable(417m);
        var week = TimeSpan.FromDays(7);

        receivable.IsOverdue(Now.AddDays(6), week).ShouldBeFalse();
        receivable.IsOverdue(Now.AddDays(7), week).ShouldBeTrue();

        receivable.Pay(417m, Now);
        receivable.IsOverdue(Now.AddDays(30), week).ShouldBeFalse();
    }

    [Fact]
    public void A_row_pays_its_parcel_once()
    {
        var receivable = Receivable(417m);
        var remittance = CodRemittance.Create("UTR123", new DateOnly(2026, 9, 25), "report.csv", [("AWB1", 417m)], "finance", Now);
        var line = remittance.Lines.Single();

        line.MatchTo(receivable, Now);
        line.MatchTo(receivable, Now);

        receivable.Received.ShouldBe(417m);
        line.ReceivableId.ShouldBe(receivable.PublicId);
    }

    [Fact]
    public void The_report_is_read_by_what_its_headings_say()
    {
        const string csv = "\uFEFFCRF ID,AWB Code,Order ID,COD Amount,Remitted Amount,Remittance Date\r\n"
            + "CRF1,AWB1,UPB-1,\"1,234.50\",\"₹ 1,234.50\",2026-09-25\r\n"
            + "CRF1,,UPB-2,10,10,2026-09-25\r\n"
            + "CRF1,AWB2,UPB-3,99,Rs. 90,2026-09-25\r\n";

        var rows = CodRemittanceCsv.Parse(csv).Value;

        rows.ShouldBe([("AWB1", 1234.50m), ("AWB2", 90m)]);
    }

    [Theory]
    [InlineData("Order,Total\nUPB-1,10\n")]
    [InlineData("AWB,Amount\n")]
    [InlineData("AWB,Amount\nAWB1,ten\n")]
    public void A_report_that_cannot_be_read_says_why(string csv)
    {
        CodRemittanceCsv.Parse(csv).Error.Code.ShouldBe("shipping.cod.file_unreadable");
    }

    private static CodReceivable Receivable(decimal cod)
    {
        var shipment = Shipment.Create(Guid.NewGuid(), "UPB-260925-ABCDEF", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Fake", "Warehouse", (500, 20m, 15m, 10m), cod);
        shipment.RecordAwb("AWB1", "Fake Courier");

        return CodReceivable.Open(shipment, Now);
    }
}
