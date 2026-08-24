using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Domain;

public static class PaymentErrors
{
    public static readonly Error NotFound =
        Error.NotFound("payments.payment.not_found", "The payment does not exist.");

    public static readonly Error NotCapturable =
        Error.Conflict("payments.payment.not_capturable", "Only a created payment can be captured.");

    public static readonly Error NotRefundable =
        Error.Conflict("payments.payment.not_refundable", "Only a captured payment can be refunded.");

    public static readonly Error RefundExceedsCaptured = Error.Conflict(
        "payments.refund.exceeds_captured",
        "The refund amount exceeds the remaining captured amount.");

    public static readonly Error InvalidAmount =
        Error.Validation("payments.amount.invalid", "Amount must be greater than zero.");

    public static readonly Error InvalidSignature =
        Error.Forbidden("payments.webhook.invalid_signature", "The webhook signature did not verify.");

    public static readonly Error ConcurrencyConflict =
        Error.Conflict("payments.payment.concurrency", "The payment changed concurrently; retry.");

    public static readonly Error IdempotencyConflict = Error.Conflict(
        "payments.idempotency_conflict",
        "This idempotency key was already used with a different request body.");
}
