using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;
using UPBazaar.Modules.Inventory.Contracts;
using UPBazaar.Modules.Inventory.Contracts.Dtos;

namespace UPBazaar.Modules.Catalog.Application;

/// <summary>Maps catalogue entities to the DTOs the API exposes.</summary>
internal static class CatalogMappings
{
    /// <summary>Requires <see cref="Category.Parent"/> to be loaded when the category has one.</summary>
    public static CategoryDto ToDto(this Category category) => new(
        category.PublicId,
        category.Name,
        category.Slug,
        category.Parent?.PublicId);

    /// <summary>
    /// Requires <see cref="Product.Category"/> and its parent to be loaded. Stock comes from
    /// Inventory, which owns it; the DTO keeps the fields so callers did not have to change.
    /// <paramref name="now"/> decides the current price and whether a sale is running.
    /// </summary>
    public static ProductDto ToDto(this Product product, StockLevelDto stock, DateTime now) => new(
        product.PublicId,
        product.Sku,
        product.Name,
        product.Slug,
        product.Brand,
        product.Description,
        product.Price,
        product.Currency,
        product.Status.ToString(),
        product.SellerId,
        product.Category.ToDto(),
        stock.OnHandQuantity,
        stock.ReservedQuantity,
        product.CreatedAtUtc,
        product.ModifiedAtUtc,
        product.ToPackageDto(),
        product.ReviewNote,
        product.PriceAt(now),
        product.ToSaleDto(now));

    /// <summary>The product's sale, or null when none is set or the one set has ended.</summary>
    public static ProductSaleDto? ToSaleDto(this Product product, DateTime now) =>
        product is { SalePrice: { } price, SaleStartsAtUtc: { } starts, SaleEndsAtUtc: { } ends } && ends > now
            ? new ProductSaleDto(price, starts, ends, product.IsSaleRunning(now))
            : null;

    /// <summary>The product's parcel, or null while its seller has not measured it.</summary>
    public static ProductPackageDto? ToPackageDto(this Product product) =>
        product is { WeightGrams: { } weight, LengthCm: { } length, BreadthCm: { } breadth, HeightCm: { } height }
            ? new ProductPackageDto(weight, length, breadth, height)
            : null;

    /// <summary>One product with its stock, asked of Inventory.</summary>
    public static async Task<ProductDto> ToDtoAsync(
        this Product product,
        IInventoryService inventory,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var levels = await inventory.GetStockLevelsAsync([product.PublicId], cancellationToken);

        return product.ToDto(levels[product.PublicId], now);
    }
}
