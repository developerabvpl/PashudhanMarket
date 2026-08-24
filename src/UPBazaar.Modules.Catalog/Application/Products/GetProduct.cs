using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Application.Products;

public sealed record GetProductQuery(Guid ProductId) : IQuery<ProductDto>;

internal sealed class GetProductQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<GetProductQuery, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(
        GetProductQuery query,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>()
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.PublicId == query.ProductId, cancellationToken);

        return product is null
            ? Result.Failure<ProductDto>(CatalogErrors.ProductNotFound)
            : product.ToDto();
    }
}
