using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Cart.Domain;

/// <summary>Every failure this module can return.</summary>
public static class CartErrors
{
    public static readonly Error NotSignedIn = Error.Unauthorized(
        "cart.not_signed_in",
        "Sign in to use a saved cart.");

    public static readonly Error ProductNotOnSale = Error.Conflict(
        "cart.product.not_on_sale",
        "This product cannot be bought right now.");

    public static readonly Error InsufficientStock = Error.Conflict(
        "cart.product.insufficient_stock",
        "There are not that many in stock.");

    public static readonly Error QuantityOutOfRange = Error.Validation(
        "cart.quantity.out_of_range",
        $"Quantity must be between 0 and {ShoppingCart.MaxQuantityPerLine}.");

    public static readonly Error TooManyLines = Error.Conflict(
        "cart.too_many_lines",
        $"A cart can hold at most {ShoppingCart.MaxLines} different products.");

    public static readonly Error ConcurrentChange = Error.Conflict(
        "cart.concurrent_change",
        "Your cart was changed somewhere else at the same time. Reload it and try again.");

    public static readonly Error NotReadyForCheckout = Error.Conflict(
        "cart.not_ready_for_checkout",
        "Some items in your cart have changed. Review your cart before checking out.");

    public static readonly Error Empty = Error.Conflict(
        "cart.empty",
        "Your cart is empty.");
}
