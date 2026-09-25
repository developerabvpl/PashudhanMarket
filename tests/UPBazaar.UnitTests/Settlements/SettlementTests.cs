using UPBazaar.Modules.Settlements.Domain;

namespace UPBazaar.UnitTests.Settlements;

public sealed class SettlementTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Seller = Guid.NewGuid();

    [Fact]
    public void An_earning_takes_commission_and_taxes_rounded_to_the_paisa_and_the_lines_add_up()
    {
        var earning = Earn(333.33m, new EarningRates(10m, 0.5m, 0.1m));

        earning.CommissionAmount.ShouldBe(33.33m);
        earning.TcsAmount.ShouldBe(1.67m);
        earning.TdsAmount.ShouldBe(0.33m);
        earning.NetAmount.ShouldBe(298.00m);
        (earning.CommissionAmount + earning.TcsAmount + earning.TdsAmount + earning.NetAmount).ShouldBe(earning.GrossAmount);
    }

    [Fact]
    public void A_delivery_share_pays_no_commission_but_has_the_taxes_withheld()
    {
        var earning = Earning.ForDelivery(
            Seller, Guid.NewGuid(), "UPB-1", Guid.NewGuid(), 49m, "INR", Now, Now.AddDays(7), new EarningRates(10m, 1m, 1m));

        earning.Kind.ShouldBe(EarningKind.Delivery);
        earning.CommissionAmount.ShouldBe(0m);
        earning.TcsAmount.ShouldBe(0.49m);
        earning.TdsAmount.ShouldBe(0.49m);
        earning.NetAmount.ShouldBe(48.02m);
    }

    [Fact]
    public void An_earning_is_payable_only_once_the_return_window_has_closed()
    {
        var earning = Earn(100m, new EarningRates(10m, 0m, 0m));

        earning.IsPayable(Now).ShouldBeFalse();
        earning.IsPayable(Now.AddDays(7)).ShouldBeTrue();
    }

    [Fact]
    public void A_return_holds_the_earning_a_refusal_releases_it_and_an_acceptance_cancels_it()
    {
        var earning = Earn(100m, new EarningRates(10m, 0m, 0m));

        earning.Hold();
        earning.Status.ShouldBe(EarningStatus.OnHold);
        earning.IsPayable(Now.AddDays(8)).ShouldBeFalse();

        earning.Release();
        earning.Status.ShouldBe(EarningStatus.Accruing);

        earning.Hold();
        earning.Cancel();
        earning.Status.ShouldBe(EarningStatus.Cancelled);

        // A cancelled sale stays cancelled, whatever arrives late.
        earning.Release();
        earning.Status.ShouldBe(EarningStatus.Cancelled);
    }

    [Fact]
    public void A_payout_totals_its_earnings_settles_them_and_is_paid_once()
    {
        var first = Earn(100m, new EarningRates(10m, 1m, 0m));
        var second = Earn(250m, new EarningRates(10m, 1m, 0m));

        var payout = Payout.Create(Seller, "UP Gaushala", "UP Gaushala Collective", "123456789012", "SBIN0001234", [first, second]);

        payout.GrossAmount.ShouldBe(350m);
        payout.CommissionAmount.ShouldBe(35m);
        payout.TcsAmount.ShouldBe(3.5m);
        payout.NetAmount.ShouldBe(311.5m);
        payout.EarningCount.ShouldBe(2);
        first.Status.ShouldBe(EarningStatus.Settled);

        // Settled means paid out: a return can no longer take it back.
        first.Hold();
        first.Status.ShouldBe(EarningStatus.Settled);

        payout.MarkPaid(" N123456789012345 ", "finance", Now).IsSuccess.ShouldBeTrue();
        payout.Utr.ShouldBe("N123456789012345");
        payout.MarkPaid("OTHER", "finance", Now).Error.ShouldBe(SettlementErrors.AlreadyPaid);
    }

    [Fact]
    public void A_payout_takes_only_its_own_sellers_earnings()
    {
        var mine = Earn(100m, new EarningRates(10m, 0m, 0m));
        var theirs = Earning.Create(Guid.NewGuid(), Guid.NewGuid(), "UPB-260923-XXXXXX", Guid.NewGuid(), 50m, "INR", Now, Now.AddDays(7), new EarningRates(10m, 0m, 0m));

        Should.Throw<ArgumentException>(() => Payout.Create(Seller, "Shop", "Holder", "1234", "SBIN0001234", [mine, theirs]));
    }

    [Fact]
    public void Courier_costs_come_off_a_payout_as_their_own_line()
    {
        var sale = Earn(300m, new EarningRates(10m, 0m, 0m));
        var courier = Earning.ForCourierCost(
            Seller, sale.OrderId, sale.OrderNumber, sale.OrderPartId, "Delivery", 90m, "INR", Now, "s1:Delivery:1");

        courier.NetAmount.ShouldBe(-90m);
        courier.CommissionAmount.ShouldBe(0m);

        var payout = Payout.Create(Seller, "UP Gaushala", "UP Gaushala Collective", "123456789012", "SBIN0001234", [sale, courier]);

        payout.GrossAmount.ShouldBe(300m);
        payout.CommissionAmount.ShouldBe(30m);
        payout.CourierCostAmount.ShouldBe(90m);
        payout.NetAmount.ShouldBe(180m);
        payout.EarningCount.ShouldBe(1);
    }

    [Fact]
    public void Nothing_is_paid_out_when_courier_costs_outweigh_the_earnings()
    {
        var sale = Earn(50m, new EarningRates(10m, 0m, 0m));
        var courier = Earning.ForCourierCost(
            Seller, Guid.NewGuid(), "UPB-1", Guid.NewGuid(), "Rto", 60m, "INR", Now, "s2:Rto:1");

        Should.Throw<ArgumentException>(() => Payout.Create(Seller, "Shop", "Holder", "1234", "SBIN0001234", [sale, courier]));
    }

    [Fact]
    public void The_default_rates_cannot_leave_a_seller_nothing()
    {
        var policy = SettlementPolicy.CreateDefault();

        policy.DefaultCommissionPercent.ShouldBe(SettlementPolicy.StartingCommissionPercent);
        policy.TcsPercent.ShouldBe(0m);
        policy.Change(90m, 5m, 5m).Error.ShouldBe(SettlementErrors.RatesTooHigh);
        policy.Change(12.5m, 0.5m, 0.1m).IsSuccess.ShouldBeTrue();
    }

    private static Earning Earn(decimal gross, EarningRates rates) =>
        Earning.Create(Seller, Guid.NewGuid(), "UPB-260923-ABCDEF", Guid.NewGuid(), gross, "INR", Now, Now.AddDays(7), rates);
}
