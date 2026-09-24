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
