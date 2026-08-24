using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Domain;

public static class CatalogErrors
{
    public static readonly Error ProductNotFound =
        Error.NotFound("catalog.product.not_found", "The product does not exist.");

    public static readonly Error CategoryNotFound =
        Error.NotFound("catalog.category.not_found", "The category does not exist.");

    public static readonly Error DuplicateSku =
        Error.Conflict("catalog.product.duplicate_sku", "A product with this SKU already exists.");

    public static readonly Error InsufficientStock =
        Error.Conflict("catalog.stock.insufficient", "Not enough stock is available.");

    public static readonly Error InvalidQuantity =
        Error.Validation("catalog.stock.invalid_quantity", "Quantity must be greater than zero.");

    public static readonly Error NothingToRelease =
        Error.Conflict("catalog.stock.nothing_to_release", "Cannot release more than is reserved.");

    public static readonly Error OnHandBelowReserved =
        Error.Conflict("catalog.stock.below_reserved", "On-hand cannot drop below reserved quantity.");

    public static readonly Error ProductNotPurchasable =
        Error.Conflict("catalog.product.not_purchasable", "The product is not available for purchase.");

    public static readonly Error ConcurrencyConflict =
        Error.Conflict("catalog.stock.concurrency", "Stock changed concurrently; retry the request.");
}
