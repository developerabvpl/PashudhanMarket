using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;

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

    /// <summary>Requires <see cref="Product.Category"/> and its parent to be loaded.</summary>
    public static ProductDto ToDto(this Product product) => new(
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
        product.OnHandQuantity,
        product.ReservedQuantity,
        product.CreatedAtUtc,
        product.ModifiedAtUtc);
}
