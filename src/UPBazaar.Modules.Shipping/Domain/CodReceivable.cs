using UPBazaar.Modules.Shipping.Contracts.Events;
using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Domain;

/// <summary>Where the cash for a delivered cash-on-delivery parcel stands. Stored by name.</summary>
public enum CodStatus
{
    /// <summary>Collected at the door; the courier has paid none of it over.</summary>
    Outstanding = 0,

    /// <summary>The courier has paid over some of it, not all.</summary>
    ShortPaid = 1,

    /// <summary>Paid over in full.</summary>
    Received = 2,

    /// <summary>Paid over, and more besides - a mistake on the courier's side to raise with it.</summary>
    Over = 3,

    /// <summary>Staff gave up on what was missing, saying why. Counts as in: the seller is still paid.</summary>
    WrittenOff = 4,
}

/// <summary>
/// The cash a courier collected for one delivered cash-on-delivery parcel, which it owes the
/// platform until its remittance pays it over. Opened when the courier reports the delivery.
///
/// The seller's earnings from the parcel wait for this: once the cash is in - paid in full, or
/// written off - they can be paid out. A shortfall is the platform's to take up with the courier,
/// never the seller's.
/// </summary>
public sealed class CodReceivable : AggregateRoot
{
    private CodReceivable()
    {
    }

    public long ShipmentId { get; private set; }

    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    public Guid OrderPartId { get; private set; }

    public Guid SellerId { get; private set; }

    /// <summary>How the courier's remittance reports name the parcel.</summary>
    public string Awb { get; private set; } = string.Empty;

    /// <summary>What the courier collected at the door.</summary>
    public decimal Expected { get; private set; }

    /// <summary>What it has paid over so far, across remittances.</summary>
    public decimal Received { get; private set; }

    public CodStatus Status { get; private set; }

    public DateTime DeliveredAtUtc { get; private set; }

    /// <summary>When the cash came to count as in: paid in full, or written off.</summary>
    public DateTime? CashInAtUtc { get; private set; }

    public string? WriteOffNote { get; private set; }

    public string? WrittenOffBy { get; private set; }

    /// <summary>Optimistic concurrency: two uploads paying the same parcel must not lose one.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Paid in full, over, or written off: nothing more to wait for.</summary>
    public bool IsCashIn => Status is CodStatus.Received or CodStatus.Over or CodStatus.WrittenOff;

    /// <summary>For a parcel the courier has just delivered, collecting cash.</summary>
    public static CodReceivable Open(Shipment shipment, DateTime deliveredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        if (shipment.Awb is null || shipment.CodAmount <= 0)
        {
            throw new ArgumentException("Only a booked cash-on-delivery parcel is owed for.", nameof(shipment));
        }

        return new CodReceivable
        {
            ShipmentId = shipment.Id,
            OrderId = shipment.OrderId,
            OrderNumber = shipment.OrderNumber,
            OrderPartId = shipment.OrderPartId,
            SellerId = shipment.SellerId,
            Awb = shipment.Awb,
            Expected = shipment.CodAmount,
            Status = CodStatus.Outstanding,
            DeliveredAtUtc = deliveredAtUtc,
        };
    }

    /// <summary>Still owed, and delivered at least <paramref name="overdueAfter"/> ago.</summary>
    public bool IsOverdue(DateTime now, TimeSpan overdueAfter) => !IsCashIn && DeliveredAtUtc + overdueAfter <= now;

    /// <summary>
    /// A remittance paid <paramref name="amount"/> for this parcel. Added to what came before, since
    /// a courier that paid short may pay the rest in a later remittance.
    /// </summary>
    public void Pay(decimal amount, DateTime now)
    {
        var wasIn = IsCashIn;

        Received += amount;

        if (Status != CodStatus.WrittenOff)
        {
            Status = Received switch
            {
                var r when r <= 0m => CodStatus.Outstanding,
                var r when r < Expected => CodStatus.ShortPaid,
                var r when r == Expected => CodStatus.Received,
                _ => CodStatus.Over,
            };
        }

        if (!wasIn && IsCashIn)
        {
            CashIn(now);
        }
    }

    /// <summary>
    /// Staff give up on what the courier has not paid - after taking it up with the courier - and
    /// say why. The seller is paid all the same.
    /// </summary>
    public Result WriteOff(string note, string? by, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        if (IsCashIn)
        {
            return Result.Failure(ShippingErrors.CodNothingOwed);
        }

        Status = CodStatus.WrittenOff;
        WriteOffNote = note.Trim();
        WrittenOffBy = by;
        CashIn(now);

        return Result.Success();
    }

    private void CashIn(DateTime now)
    {
        CashInAtUtc = now;
        Raise(new CodCashReceivedDomainEvent(OrderId, OrderPartId, SellerId));
    }
}
