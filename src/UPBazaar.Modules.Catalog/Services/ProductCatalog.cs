using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Contracts;
using UPBazaar.Modules.Catalog.Domain;

namespace UPBazaar.Modules.Catalog.Services;

/// <summary>The catalogue side of cross-module conversations.</summary>
internal sealed class ProductCatalog(UPBazaarDbContext dbContext) : IProductCatalog
{
    public Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.Set<Product>().AnyAsync(p => p.PublicId == productId, cancellationToken);
}
