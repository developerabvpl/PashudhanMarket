using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Orders.Contracts.Events;

/// <summary>
/// An order was placed. A cash-on-delivery order is confirmed at the same moment; an online one
/// waits for <see cref="OrderConfirmedDomainEvent"/>. Notifications sends "we have your order" from this.
/// </summary>
public sealed record OrderPlacedDomainEvent(
    Guid OrderId,
    string Number,
    Guid BuyerId,
    string PaymentMethod,
    decimal Total,
    string Currency) : DomainEvent;

/// <summary>
/// The order can be fulfilled: paid online, or placed as cash on delivery. Its stock is
/// committed. Shipping creates one Shiprocket shipment per seller part from this.
/// </summary>
public sealed record OrderConfirmedDomainEvent(
    Guid OrderId,
    string Number,
    IReadOnlyList<Guid> PartIds) : DomainEvent;

/// <summary>
/// One seller's part was cancelled. <see cref="RefundDue"/> is what Payments owes the buyer for
/// it: the part's subtotal if the order was paid online, otherwise zero.
/// </summary>
public sealed record OrderPartCancelledDomainEvent(
    Guid OrderId,
    string Number,
    Guid PartId,
    Guid SellerId,
    decimal RefundDue,
    string Currency) : DomainEvent;

/// <summary>
/// A seller's part came back undelivered (RTO) and is with the seller again. <see cref="RefundDue"/>
/// is the part's subtotal if the order was paid online, otherwise zero - cash on delivery collected
/// nothing.
/// </summary>
public sealed record OrderPartReturnedDomainEvent(
    Guid OrderId,
    string Number,
    Guid PartId,
    Guid SellerId,
    decimal RefundDue,
    string Currency) : DomainEvent;

/// <summary>
/// Nothing in the order is coming any more: the buyer or staff cancelled it, or an online payment
/// never arrived. Each part cancelled along the way also raised <see cref="OrderPartCancelledDomainEvent"/>.
/// </summary>
public sealed record OrderCancelledDomainEvent(
    Guid OrderId,
    string Number,
    string Reason) : DomainEvent;
