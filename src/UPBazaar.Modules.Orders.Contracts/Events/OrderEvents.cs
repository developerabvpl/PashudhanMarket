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
/// A seller's part is back with the seller: it came back undelivered (RTO), or the buyer sent it
/// back (<see cref="RequestedByBuyer"/>).
///
/// <see cref="RefundDue"/> is what Payments owes the buyer. After an RTO it is the part's subtotal
/// if the order was paid online, else zero, since cash on delivery collected nothing. After a
/// buyer's return it is the subtotal either way, paid to <see cref="RefundUpiId"/> when the order
/// was cash on delivery and back through the online payment otherwise.
///
/// The last two parameters default so that events written to the outbox before they existed
/// still read as RTOs.
/// </summary>
public sealed record OrderPartReturnedDomainEvent(
    Guid OrderId,
    string Number,
    Guid PartId,
    Guid SellerId,
    decimal RefundDue,
    string Currency,
    bool RequestedByBuyer = false,
    string? RefundUpiId = null) : DomainEvent;

/// <summary>
/// A buyer asked to return a delivered part. Nothing acts on it yet; it is here for the seller's
/// notification once Notifications sends them.
/// </summary>
public sealed record OrderPartReturnRequestedDomainEvent(
    Guid OrderId,
    string Number,
    Guid PartId,
    Guid SellerId,
    string Reason) : DomainEvent;

/// <summary>
/// A buyer's return was accepted and the part is going back. Shipping books a pickup from the
/// buyer's address from this.
/// </summary>
public sealed record OrderPartReturnApprovedDomainEvent(
    Guid OrderId,
    string Number,
    Guid PartId,
    Guid SellerId) : DomainEvent;

/// <summary>
/// Nothing in the order is coming any more: the buyer or staff cancelled it, or an online payment
/// never arrived. Each part cancelled along the way also raised <see cref="OrderPartCancelledDomainEvent"/>.
/// </summary>
public sealed record OrderCancelledDomainEvent(
    Guid OrderId,
    string Number,
    string Reason) : DomainEvent;
