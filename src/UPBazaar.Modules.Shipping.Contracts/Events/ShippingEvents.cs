using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Shipping.Contracts.Events;

/// <summary>
/// The courier collected a parcel for the buyer and it is on its way. Raised once, the first time
/// the courier reports it moving; the buyer is told, with the AWB to follow it by.
/// </summary>
/// <param name="ShipmentId">The shipment.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="OrderPartId">The parcel's part.</param>
/// <param name="BuyerId">Who it is going to.</param>
/// <param name="CourierName">The courier carrying it.</param>
/// <param name="Awb">The courier's tracking number.</param>
public sealed record ShipmentDispatchedDomainEvent(
    Guid ShipmentId,
    Guid OrderId,
    string OrderNumber,
    Guid OrderPartId,
    Guid BuyerId,
    string? CourierName,
    string? Awb) : DomainEvent;

/// <summary>
/// A courier trip was made, or the charge for one was corrected: Settlements decides who pays it.
/// Amounts are the difference from what was charged before, so a correction can be negative.
/// </summary>
/// <param name="ShipmentId">The shipment.</param>
/// <param name="OrderId">Its order.</param>
/// <param name="OrderNumber">The order's number.</param>
/// <param name="OrderPartId">The seller's part it carried.</param>
/// <param name="SellerId">Whose parcel it is.</param>
/// <param name="Trip">Delivery, Rto (the courier bringing an undelivered parcel back) or ReturnPickup (a buyer's return).</param>
/// <param name="Amount">What is charged now: the trip's charge, or a correction to it.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="Sequence">1 for the trip's charge, 2 onwards for each correction; with the shipment and trip, it names this charge uniquely.</param>
/// <param name="ReturnReason">For a return pickup, why the buyer sent it back.</param>
public sealed record ShipmentChargedDomainEvent(
    Guid ShipmentId,
    Guid OrderId,
    string OrderNumber,
    Guid OrderPartId,
    Guid SellerId,
    string Trip,
    decimal Amount,
    string Currency,
    int Sequence,
    string? ReturnReason) : DomainEvent;

/// <summary>
/// The cash a courier collected for a delivered cash-on-delivery parcel is in: paid over in full,
/// or written off by staff as never coming. Settlements lets the seller's earnings from the parcel
/// become payable - the platform does not pay out money it has not received.
/// </summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderPartId">The seller's part the parcel carried.</param>
/// <param name="SellerId">Whose parcel it was.</param>
public sealed record CodCashReceivedDomainEvent(Guid OrderId, Guid OrderPartId, Guid SellerId) : DomainEvent;
