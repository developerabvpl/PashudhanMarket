using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;

namespace UPBazaar.Modules.Catalog.Application;

internal static class CatalogMappings
{
    public static ProductDto ToDto(this Product product) => new(
        product.PublicId,
        product.Sku,
        product.Name,
        product.Slug,
        product.Description,
        product.Price,
        product.Currency,
        product.Status.ToString(),
        product.SellerId,
        new CategoryDto(
            product.Category.PublicId,
            product.Category.Name,
            product.Category.Slug,
            null),
        product.Stock.OnHand,
        product.Stock.Reserved,
        product.CreatedAtUtc,
        product.ModifiedAtUtc);

    public static ProductSummaryDto ToSummary(this Product product) => new(
        product.PublicId,
        product.Sku,
        product.Name,
        product.Price,
        product.Currency,
        product.Status.ToString(),
        product.Stock.Available);

    /// <summary>URL-safe slug derived from the product name.</summary>
    public static string ToSlug(string value)
    {
        var characters = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        var slug = new string(characters);

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }
}
