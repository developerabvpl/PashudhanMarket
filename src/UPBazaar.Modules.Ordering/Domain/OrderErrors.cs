using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Ordering.Domain;

public static class OrderErrors
{
    public static readonly Error NotFound =
        Error.NotFound("ordering.order.not_found", "The order does not exist.");

    public static readonly Error EmptyCart =
        Error.Validation("ordering.checkout.empty_cart", "An order needs at least one line.");

    public static readonly Error MixedCurrency =
        Error.Validation("ordering.checkout.mixed_currency", "All lines must share one currency.");

    public static readonly Error IdempotencyConflict = Error.Conflict(
        "ordering.checkout.idempotency_conflict",
        "This idempotency key was already used with a different request body.");

    public static readonly Error CheckoutInFlight = Error.Conflict(
        "ordering.checkout.in_flight",
        "The original request with this idempotency key is still being processed.");

    public static readonly Error NotCancellable =
        Error.Conflict("ordering.order.not_cancellable", "Only an unpaid order can be cancelled.");

    public static readonly Error AlreadyPaid =
        Error.Conflict("ordering.order.already_paid", "The order is already paid.");
}
