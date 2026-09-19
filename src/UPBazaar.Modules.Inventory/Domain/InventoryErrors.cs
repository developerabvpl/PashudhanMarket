using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Inventory.Domain;

/// <summary>Every failure this module can return.</summary>
public static class InventoryErrors
{
    public static readonly Error ProductNotFound = Error.NotFound(
        "inventory.product.not_found",
        "The product does not exist in the catalogue.");

    public static readonly Error InsufficientStock = Error.Conflict(
        "inventory.stock.insufficient",
        "There is not enough stock available.");

    public static readonly Error BelowReserved = Error.Conflict(
        "inventory.stock.below_reserved",
        "Stock on hand cannot fall below what is already reserved for orders.");

    public static readonly Error ConcurrentChange = Error.Conflict(
        "inventory.stock.concurrent_change",
        "Stock changed while this request was being processed. Try again.");

    public static readonly Error ReservationNotFound = Error.NotFound(
        "inventory.reservation.not_found",
        "The reservation does not exist.");

    public static readonly Error ReservationNotActive = Error.Conflict(
        "inventory.reservation.not_active",
        "The reservation has already expired or been released, so its stock may have been sold again.");

    public static readonly Error DuplicateLine = Error.Validation(
        "inventory.reservation.duplicate_line",
        "Each product may appear only once in a reservation.");
}
