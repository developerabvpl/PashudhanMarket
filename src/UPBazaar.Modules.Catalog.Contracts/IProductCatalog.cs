using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Contracts;

/// <summary>
/// Read surface other modules use instead of querying catalog tables.
/// </summary>
public interface IProductCatalog
{
    Task<Result<ProductSummaryDto>> GetProductAsync(Guid productId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ProductSummaryDto>>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);
}
