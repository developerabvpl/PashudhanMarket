using UPBazaar.Modules.Shipping.Contracts.Events;
using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Shipping.Domain;

/// <summary>Where a shipment stands. Stored by name.</summary>
public enum ShipmentStatus
{
    /// <summary>Recorded here; the carrier booking is not finished. Packing again carries on from where it stopped.</summary>
    Booking = 0,

    /// <summary>Booked, with an AWB, and a pickup requested. Waiting for the courier to collect.</summary>
    PickupRequested = 1,

    /// <summary>The courier has it.</summary>
    InTransit = 2,

    Delivered = 3,

    /// <summary>Could not be delivered, and is back with the seller.</summary>
    Returned = 4,

    Cancelled = 5,

    /// <summary>Could not be delivered, and the courier is taking it back to the seller (RTO).</summary>
    ReturnInTransit = 6,
}

/// <summary>Which way a consignment travels. Stored by name.</summary>
public enum ShipmentDirection
{
    /// <summary>From the seller to the buyer. If it cannot be delivered it comes back as an RTO, still Forward.</summary>
    Forward = 0,

    /// <summary>A buyer's return: collected from the buyer's address and taken to the seller.</summary>
    Return = 1,
}

/// <summary>
/// One courier consignment: one seller's part of one order, from booking to the door - or, for a
/// buyer's return, from the buyer's door back to the seller.
///
/// Booking with the carrier is three calls - create the order, assign an AWB, request pickup -
/// and any of them can fail. The shipment records how far it got after each one, so packing
/// the same part again resumes rather than booking a second consignment for the same goods.
/// </summary>
public sealed class Shipment : AggregateRoot, IAuditable
{
    private readonly List<ShipmentEvent> _events = [];

    private Shipment()
    {
    }

    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    public Guid OrderPartId { get; private set; }

    public Guid BuyerId { get; private set; }

    public Guid SellerId { get; private set; }

    public ShipmentStatus Status { get; private set; }

    public ShipmentDirection Direction { get; private set; }

    /// <summary>Shiprocket, or Fake in development.</summary>
    public string Carrier { get; private set; } = string.Empty;

    /// <summary>Our reference on the carrier's side: unique per consignment, since one order can ship in several.</summary>
    public string CarrierReference { get; private set; } = string.Empty;

    /// <summary>
    /// The Shiprocket pickup location the courier collects from. For a return it is
    /// <see cref="BuyerPickup"/>: the courier collects from the delivery address instead.
    /// </summary>
    public string PickupLocation { get; private set; } = string.Empty;

    public int WeightGrams { get; private set; }

    public decimal LengthCm { get; private set; }

    public decimal BreadthCm { get; private set; }

    public decimal HeightCm { get; private set; }

    /// <summary>COD or Prepaid.</summary>
    public string PaymentMode { get; private set; } = string.Empty;

    public decimal CodAmount { get; private set; }

    public string? CarrierOrderId { get; private set; }

    public string? CarrierShipmentId { get; private set; }

    /// <summary>Air waybill number: the courier's tracking number, and how its updates find this shipment.</summary>
    public string? Awb { get; private set; }

    public string? CourierName { get; private set; }

    public DateTime? PickupRequestedAtUtc { get; private set; }

    /// <summary>Why the last booking step failed, for whoever packs it again.</summary>
    public string? LastError { get; private set; }

    public IReadOnlyCollection<ShipmentEvent> Events => _events.AsReadOnly();

    /// <summary>Optimistic concurrency: a courier update and a cancellation must not both win.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>Still one the courier could act on, as opposed to finished or called off.</summary>
    public bool IsLive => Status is not (ShipmentStatus.Cancelled or ShipmentStatus.Returned or ShipmentStatus.Delivered);

    /// <summary>Booked but not yet collected, so the carrier can still cancel it.</summary>
    public bool CanCancel => Status is ShipmentStatus.Booking or ShipmentStatus.PickupRequested;

    /// <summary>What <see cref="PickupLocation"/> says on a return, which is collected from the buyer.</summary>
    public const string BuyerPickup = "Buyer's address";

    public static Shipment Create(
        Guid orderId,
        string orderNumber,
        Guid orderPartId,
        Guid buyerId,
        Guid sellerId,
        string carrier,
        string pickupLocation,
        (int WeightGrams, decimal LengthCm, decimal BreadthCm, decimal HeightCm) parcel,
        decimal codAmount) => new()
    {
        OrderId = orderId,
        OrderNumber = orderNumber,
        OrderPartId = orderPartId,
        BuyerId = buyerId,
        SellerId = sellerId,
        Carrier = carrier,
        CarrierReference = $"{orderNumber}-{orderPartId.ToString("N")[..6].ToUpperInvariant()}",
        PickupLocation = pickupLocation,
        WeightGrams = parcel.WeightGrams,
        LengthCm = parcel.LengthCm,
        BreadthCm = parcel.BreadthCm,
        HeightCm = parcel.HeightCm,
        PaymentMode = codAmount > 0 ? "COD" : "Prepaid",
        CodAmount = codAmount,
        Status = ShipmentStatus.Booking,
        Direction = ShipmentDirection.Forward,
    };

    /// <summary>
    /// A buyer's return of a delivered part. Always prepaid: the buyer pays nothing to send it back.
    /// </summary>
    public static Shipment CreateReturn(
        Guid orderId,
        string orderNumber,
        Guid orderPartId,
        Guid buyerId,
        Guid sellerId,
        string carrier,
        (int WeightGrams, decimal LengthCm, decimal BreadthCm, decimal HeightCm) parcel) => new()
    {
        OrderId = orderId,
        OrderNumber = orderNumber,
        OrderPartId = orderPartId,
        BuyerId = buyerId,
        SellerId = sellerId,
        Carrier = carrier,
        CarrierReference = $"{orderNumber}-{orderPartId.ToString("N")[..6].ToUpperInvariant()}-R",
        PickupLocation = BuyerPickup,
        WeightGrams = parcel.WeightGrams,
        LengthCm = parcel.LengthCm,
        BreadthCm = parcel.BreadthCm,
        HeightCm = parcel.HeightCm,
        PaymentMode = "Prepaid",
        CodAmount = 0m,
        Status = ShipmentStatus.Booking,
        Direction = ShipmentDirection.Return,
    };

    public void RecordCarrierOrder(string carrierOrderId, string carrierShipmentId)
    {
        CarrierOrderId = carrierOrderId;
        CarrierShipmentId = carrierShipmentId;
        LastError = null;
    }

    public void RecordAwb(string awb, string courierName)
    {
        Awb = awb;
        CourierName = courierName;
        LastError = null;
    }

    public void RecordPickupRequested(DateTime now)
    {
        PickupRequestedAtUtc = now;
        LastError = null;

        if (Status == ShipmentStatus.Booking)
        {
            Status = ShipmentStatus.PickupRequested;
        }
    }

    public void RecordBookingError(string error) => LastError = error.Length > 500 ? error[..500] : error;

    /// <summary>
    /// Applies a courier update and says whether the shipment moved. Updates only ever move a
    /// shipment forward along one of its two roads - to the buyer, or back to the seller - so a late
    /// "in transit" after "delivered" is recorded but changes nothing.
    /// </summary>
    public bool ApplyCourierStatus(string rawStatus, ShipmentStatus? mapped, DateTime now)
    {
        _events.Add(ShipmentEvent.Create(rawStatus, now));

        if (mapped is not { } next || !CanMove(Direction, Status, next))
        {
            return false;
        }

        // The first scan that has the parcel moving towards the buyer: tell them once.
        if (Direction == ShipmentDirection.Forward
            && next == ShipmentStatus.InTransit
            && Status is ShipmentStatus.Booking or ShipmentStatus.PickupRequested)
        {
            Raise(new ShipmentDispatchedDomainEvent(PublicId, OrderId, OrderNumber, OrderPartId, BuyerId, CourierName, Awb));
        }

        Status = next;

        return true;
    }

    public void Cancel() => Status = ShipmentStatus.Cancelled;

    /// <summary>
    /// Where a shipment may go from where it is. Not an ordering of the enum: delivery and return
    /// are alternative endings, and a parcel on its way back can still be reported "returned"
    /// without first being reported "in transit". A buyer's return has one road only: its
    /// "delivered" means delivered to the seller, and it has no RTO of its own.
    /// </summary>
    private static bool CanMove(ShipmentDirection direction, ShipmentStatus from, ShipmentStatus to) => (from, to) switch
    {
        _ when direction == ShipmentDirection.Return
            && to is ShipmentStatus.ReturnInTransit or ShipmentStatus.Returned => false,
        (ShipmentStatus.Booking or ShipmentStatus.PickupRequested, ShipmentStatus.Cancelled) => true,
        (ShipmentStatus.Booking or ShipmentStatus.PickupRequested, ShipmentStatus.InTransit) => true,
        (ShipmentStatus.Booking or ShipmentStatus.PickupRequested or ShipmentStatus.InTransit,
            ShipmentStatus.Delivered or ShipmentStatus.ReturnInTransit or ShipmentStatus.Returned) => true,
        (ShipmentStatus.ReturnInTransit, ShipmentStatus.Returned) => true,
        _ => false,
    };
}

/// <summary>One courier update, as the courier worded it.</summary>
public sealed class ShipmentEvent : Entity
{
    private ShipmentEvent()
    {
    }

    public long ShipmentId { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public DateTime OccurredAtUtc { get; private set; }

    internal static ShipmentEvent Create(string status, DateTime now) => new()
    {
        Status = status.Length > 64 ? status[..64] : status,
        OccurredAtUtc = now,
    };
}

/// <summary>
/// Where a courier collects from: a seller's own address, or - with no seller - the platform
/// warehouse that every seller without one of their own ships through.
///
/// Only the name is kept. The address itself is registered in the Shiprocket dashboard, which
/// verifies it; Shiprocket bookings refer to a pickup location by that name alone.
/// </summary>
public sealed class PickupLocation : Entity
{
    public const int NameMaxLength = 36;

    private PickupLocation()
    {
    }

    /// <summary>The seller, or null for the platform warehouse.</summary>
    public Guid? SellerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; private set; }

    public static PickupLocation Create(Guid? sellerId, string name, DateTime now) => new()
    {
        SellerId = sellerId,
        Name = name.Trim(),
        UpdatedAtUtc = now,
    };

    public void Rename(string name, DateTime now)
    {
        Name = name.Trim();
        UpdatedAtUtc = now;
    }
}

/// <summary>
/// Shiprocket's status wording, reduced to what moves a shipment. Anything not listed - "AWB
/// ASSIGNED", "PICKUP SCHEDULED", "OUT FOR PICKUP" and the rest - is recorded and moves nothing.
/// </summary>
public static class CourierStatus
{
    private static readonly string[] InTransit =
    [
        "PICKED UP",
        "SHIPPED",
        "IN TRANSIT",
        "OUT FOR DELIVERY",
        "REACHED AT DESTINATION HUB",
        "REACHED DESTINATION HUB",
    ];

    public static ShipmentStatus? Map(string rawStatus)
    {
        var status = rawStatus.Trim().ToUpperInvariant();

        return status switch
        {
            "DELIVERED" => ShipmentStatus.Delivered,
            "CANCELED" or "CANCELLED" => ShipmentStatus.Cancelled,
            "RTO DELIVERED" => ShipmentStatus.Returned,
            _ when status.StartsWith("RTO", StringComparison.Ordinal) => ShipmentStatus.ReturnInTransit,
            _ when InTransit.Contains(status) => ShipmentStatus.InTransit,
            _ => null,
        };
    }

    /// <summary>
    /// The same for a buyer's return, whose updates Shiprocket may word with a "RETURN" prefix -
    /// "RETURN PICKED UP", "RETURN DELIVERED" - and whose "delivered" means back with the seller.
    /// </summary>
    public static ShipmentStatus? MapReturn(string rawStatus)
    {
        var status = rawStatus.Trim().ToUpperInvariant();

        if (status.StartsWith("RETURN ", StringComparison.Ordinal))
        {
            status = status["RETURN ".Length..].Trim();
        }

        return status switch
        {
            "DELIVERED" => ShipmentStatus.Delivered,
            "CANCELED" or "CANCELLED" => ShipmentStatus.Cancelled,
            _ when InTransit.Contains(status) => ShipmentStatus.InTransit,
            _ => null,
        };
    }
}
