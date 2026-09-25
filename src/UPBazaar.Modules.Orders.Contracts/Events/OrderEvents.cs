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
/// it if the order was paid online, otherwise zero: the part's subtotal, plus its share of the
/// delivery charge when no other part is left to carry that share.
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
/// A seller's part reached the buyer. Settlements starts the seller's earning from it, payable once
/// <see cref="ReturnWindowClosesAtUtc"/> has passed with no return open.
/// </summary>
/// <param name="OrderId">The order.</param>
/// <param name="Number">Its number.</param>
/// <param name="PartId">The part delivered.</param>
/// <param name="SellerId">Whose goods.</param>
/// <param name="Subtotal">The part's goods at full price, before any coupon.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="DeliveredAtUtc">When the courier delivered it.</param>
/// <param name="ReturnWindowClosesAtUtc">The last moment the buyer may ask to return it.</param>
/// <param name="DeliveryFee">The part's share of the delivery charge the buyer paid, which the seller earns.</param>
/// <param name="SellerDiscount">The coupon discount on the part's goods when the seller bears it: their earning is on the goods less this. Zero when the platform bears the discount, or there was none.</param>
public sealed record OrderPartDeliveredDomainEvent(
    Guid OrderId,
    string Number,
    Guid PartId,
    Guid SellerId,
    decimal Subtotal,
    string Currency,
    DateTime DeliveredAtUtc,
    DateTime ReturnWindowClosesAtUtc,
    decimal DeliveryFee = 0m,
    decimal SellerDiscount = 0m) : DomainEvent;

/// <summary>
/// A buyer asked to return a delivered part. Settlements holds the seller's earning from it until
/// the request is decided; Notifications will tell the seller, once it sends anything.
/// </summary>
public sealed record OrderPartReturnRequestedDomainEvent(
    Guid OrderId,
    string Number,
    Guid PartId,
    Guid SellerId,
    string Reason) : DomainEvent;

/// <summary>
/// A buyer's return was refused and the part stays with the buyer. Settlements releases the
/// seller's earning it was holding.
/// </summary>
public sealed record OrderPartReturnRejectedDomainEvent(
    Guid OrderId,
    string Number,
    Guid PartId,
    Guid SellerId) : DomainEvent;

/// <summary>
/// A buyer's return was accepted and the part is going back. Shipping books a pickup from the
/// buyer's address from this; Settlements cancels the seller's earning, since the sale is undone.
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
