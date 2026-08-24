using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Application;
using UPBazaar.Modules.Catalog.Contracts;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Services;

/// <summary>
/// The catalog side of every cross-module conversation. Other modules hold the interfaces
/// from Contracts and never see Product or Stock.
/// </summary>
internal sealed class ProductCatalogService(UPBazaarDbContext dbContext)
    : IProductCatalog, IStockReservations
{
    public async Task<Result<ProductSummaryDto>> GetProductAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PublicId == productId, cancellationToken);

        return product is null
            ? Result.Failure<ProductSummaryDto>(CatalogErrors.ProductNotFound)
            : product.ToSummary();
    }

    public async Task<Result<IReadOnlyList<ProductSummaryDto>>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var products = await dbContext.Set<Product>()
            .AsNoTracking()
            .Where(p => productIds.Contains(p.PublicId))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ProductSummaryDto>>(
            products.Select(p => p.ToSummary()).ToList());
    }

    public async Task<Result<IReadOnlyList<ReservedItemDto>>> ReserveAsync(
        IReadOnlyCollection<StockReservationLine> lines,
        CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return Result.Failure<IReadOnlyList<ReservedItemDto>>(CatalogErrors.InvalidQuantity);
        }

        var requestedIds = lines.Select(l => l.ProductId).Distinct().ToList();

        var products = await dbContext.Set<Product>()
            .Where(p => requestedIds.Contains(p.PublicId))
            .ToDictionaryAsync(p => p.PublicId, cancellationToken);

        var reserved = new List<ReservedItemDto>(lines.Count);

        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                return Result.Failure<IReadOnlyList<ReservedItemDto>>(CatalogErrors.ProductNotFound);
            }

            if (!product.IsPurchasable)
            {
                return Result.Failure<IReadOnlyList<ReservedItemDto>>(CatalogErrors.ProductNotPurchasable);
            }

            var reservation = product.Stock.Reserve(line.Quantity);

            if (reservation.IsFailure)
            {
                return Result.Failure<IReadOnlyList<ReservedItemDto>>(reservation.Error);
            }

            reserved.Add(new ReservedItemDto(
                product.PublicId,
                product.Sku,
                product.Name,
                product.Price,
                product.Currency,
                line.Quantity));
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone reserved the same units first; the caller retries the whole checkout.
            return Result.Failure<IReadOnlyList<ReservedItemDto>>(CatalogErrors.ConcurrencyConflict);
        }

        return Result.Success<IReadOnlyList<ReservedItemDto>>(reserved);
    }

    public async Task<Result> ReleaseAsync(
        IReadOnlyCollection<StockReservationLine> lines,
        CancellationToken cancellationToken)
    {
        var requestedIds = lines.Select(l => l.ProductId).Distinct().ToList();

        var products = await dbContext.Set<Product>()
            .Where(p => requestedIds.Contains(p.PublicId))
            .ToDictionaryAsync(p => p.PublicId, cancellationToken);

        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                return Result.Failure(CatalogErrors.ProductNotFound);
            }

            var release = product.Stock.Release(line.Quantity);

            if (release.IsFailure)
            {
                return release;
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(CatalogErrors.ConcurrencyConflict);
        }

        return Result.Success();
    }
}
