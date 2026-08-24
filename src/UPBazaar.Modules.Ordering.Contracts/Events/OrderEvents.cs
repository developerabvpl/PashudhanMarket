using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Ordering.Contracts.Events;

/// <param name="ProductId">Catalog public id, so Shipping never needs the catalog tables.</param>
public sealed record OrderLineSnapshot(Guid ProductId, string Sku, int Quantity);

/// <summary>
/// An order was accepted and its stock is held. Shipping books a consignment off this;
/// notifications go out from here too.
/// </summary>
public sealed record OrderPlacedDomainEvent(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    decimal Total,
    string Currency,
    string DeliveryPostcode,
    IReadOnlyList<OrderLineSnapshot> Lines) : DomainEvent;

public sealed record OrderCancelledDomainEvent(
    Guid OrderId,
    string OrderNumber,
    string Reason,
    IReadOnlyList<OrderLineSnapshot> Lines) : DomainEvent;

public sealed record OrderPaidDomainEvent(Guid OrderId, string OrderNumber, Guid PaymentId) : DomainEvent;
