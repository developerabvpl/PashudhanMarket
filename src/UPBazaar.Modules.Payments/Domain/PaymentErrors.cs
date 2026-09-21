using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Domain;

/// <summary>Every failure this module can return.</summary>
public static class PaymentErrors
{
    public static readonly Error NotSignedIn = Error.Unauthorized(
        "payments.not_signed_in",
        "Sign in to pay for an order.");

    // Not found rather than forbidden for somebody else's payment, so ids cannot be probed.
    public static readonly Error NotFound = Error.NotFound(
        "payments.not_found",
        "Payment not found.");

    public static readonly Error RefundNotFound = Error.NotFound(
        "payments.refund.not_found",
        "Refund not found.");

    public static readonly Error OnlineDisabled = Error.Conflict(
        "payments.online_disabled",
        "Online payment is not available right now. Choose cash on delivery.");

    public static readonly Error UnsupportedCurrency = Error.Conflict(
        "payments.unsupported_currency",
        "Only payments in Indian rupees are accepted.");

    public static readonly Error InvalidSignature = Error.Validation(
        "payments.invalid_signature",
        "The payment could not be verified.");

    public static readonly Error PaidWithAnotherPayment = Error.Conflict(
        "payments.already_paid",
        "This payment was already completed by a different transaction.");

    public static readonly Error OrderAlreadyPaid = Error.Conflict(
        "payments.order_already_paid",
        "This order has already been paid. It will be confirmed in a moment.");

    public static readonly Error RefundExceedsPayment = Error.Conflict(
        "payments.refund.exceeds_payment",
        "Refunds cannot add up to more than was paid.");

    public static readonly Error AlreadyRefunded = Error.Conflict(
        "payments.refund.already_refunded",
        "This refund has already been recorded as made.");

    public static readonly Error GatewayUnavailable = Error.Failure(
        "payments.gateway_unavailable",
        "The payment gateway could not be reached. Try again in a moment.");

    public static readonly Error ConcurrentChange = Error.Conflict(
        "payments.concurrent_change",
        "The payment was updated at the same time by another request. Try again.");

    public static readonly Error FakeGatewayOnly = Error.NotFound(
        "payments.fake_gateway_only",
        "Simulated payments exist only in development.");
}
