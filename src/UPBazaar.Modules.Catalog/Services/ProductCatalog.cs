using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Contracts;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;

namespace UPBazaar.Modules.Catalog.Services;

/// <summary>The catalogue side of cross-module conversations.</summary>
internal sealed class ProductCatalog(UPBazaarDbContext dbContext) : IProductCatalog
{
    public Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.Set<Product>().AnyAsync(p => p.PublicId == productId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, CatalogProductDto>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        return await dbContext.Set<Product>()
            .AsNoTracking()
            .Where(p => productIds.Contains(p.PublicId))
            .Select(p => new CatalogProductDto(
                p.PublicId,
                p.Sku,
                p.Name,
                p.Price,
                p.Currency,
                p.SellerId,
                p.Status == ProductStatus.Active && p.Price > 0))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, ProductPackageDto>> GetPackagesAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        return await dbContext.Set<Product>()
            .AsNoTracking()
            .Where(p => productIds.Contains(p.PublicId) && p.WeightGrams != null)
            .ToDictionaryAsync(
                p => p.PublicId,
                p => new ProductPackageDto(p.WeightGrams!.Value, p.LengthCm!.Value, p.BreadthCm!.Value, p.HeightCm!.Value),
                cancellationToken);
    }
}
