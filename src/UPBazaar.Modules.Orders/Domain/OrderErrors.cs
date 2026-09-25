using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Domain;

/// <summary>Every failure this module can return.</summary>
public static class OrderErrors
{
    public static readonly Error NotSignedIn = Error.Unauthorized(
        "orders.not_signed_in",
        "Sign in to place and see orders.");

    // Not found rather than forbidden for somebody else's order, so an order id cannot be probed.
    public static readonly Error NotFound = Error.NotFound(
        "orders.not_found",
        "Order not found.");

    public static readonly Error PartNotFound = Error.NotFound(
        "orders.part.not_found",
        "That part of the order was not found.");

    public static readonly Error MixedCurrencies = Error.Conflict(
        "orders.mixed_currencies",
        "Everything in one order must be priced in the same currency.");

    public static readonly Error NotAwaitingPayment = Error.Conflict(
        "orders.not_awaiting_payment",
        "This order is not waiting for payment.");

    public static readonly Error PaymentTooLate = Error.Conflict(
        "orders.payment_too_late",
        "The time to pay for this order has run out.");

    public static readonly Error AmountMismatch = Error.Validation(
        "orders.payment.amount_mismatch",
        "The amount paid does not match the amount due.");

    public static readonly Error AlreadyPaidElsewhere = Error.Conflict(
        "orders.payment.already_paid",
        "This order has already been paid with a different payment.");

    public static readonly Error CannotCancel = Error.Conflict(
        "orders.cannot_cancel",
        "This order can no longer be cancelled: part of it has already shipped.");

    public static readonly Error PartCannotCancel = Error.Conflict(
        "orders.part.cannot_cancel",
        "This part can no longer be cancelled.");

    public static readonly Error PartAwaitingPayment = Error.Conflict(
        "orders.part.awaiting_payment",
        "The order has not been paid yet. Cancel the whole order instead.");

    public static readonly Error InvalidTransition = Error.Conflict(
        "orders.part.invalid_transition",
        "That part of the order cannot move to this status from where it is.");

    public static readonly Error NotInTransit = Error.Conflict(
        "orders.part.not_in_transit",
        "Only a part that has left the seller can be returned.");

    public static readonly Error NotAwaitingInspection = Error.Conflict(
        "orders.part.not_awaiting_inspection",
        "Only a returned parcel not yet inspected can be inspected.");

    public static readonly Error NotDelivered = Error.Conflict(
        "orders.part.not_delivered",
        "Only a parcel that has been delivered can be returned.");

    public static readonly Error ReturnWindowClosed = Error.Conflict(
        "orders.part.return_window_closed",
        "The time to return this parcel has passed.");

    public static readonly Error ReturnAlreadyRequested = Error.Conflict(
        "orders.part.return_already_requested",
        "A return has already been asked for on this parcel.");

    public static readonly Error ReturnNotPending = Error.Conflict(
        "orders.part.return_not_pending",
        "There is no return request waiting for a decision on this parcel.");

    public static readonly Error RefundUpiIdRequired = Error.Validation(
        "orders.part.refund_upi_required",
        "This order was paid in cash. Give a UPI id to receive the refund.");

    public static readonly Error DeliveryAlreadyFree = Error.Validation(
        "orders.coupon.delivery_already_free",
        "Delivery is already free on this order, so that coupon takes nothing off.");

    public static readonly Error ConcurrentChange = Error.Conflict(
        "orders.concurrent_change",
        "The order was changed by someone else at the same time. Reload it and try again.");

    public static readonly Error OutOfStock = Error.Conflict(
        "orders.out_of_stock",
        "Some items sold out while you were checking out. Review your cart and try again.");
}
