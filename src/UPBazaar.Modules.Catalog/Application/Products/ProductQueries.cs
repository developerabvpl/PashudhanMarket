using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;
using UPBazaar.Modules.Inventory.Contracts;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Application.Products;

/// <summary>
/// A page of the catalogue.
///
/// <paramref name="IncludeUnpublished"/> is not bound from the query string: the controller sets
/// it from the caller's permissions, so a shopper cannot ask for drafts by adding a parameter.
/// </summary>
public sealed record ListProductsQuery(
    int Page,
    int PageSize,
    string? Search,
    Guid? CategoryId,
    string? Status,
    Guid? SellerId,
    bool IncludeUnpublished) : IQuery<PagedList<ProductSummaryDto>>;

internal sealed class ListProductsQueryValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(256);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<ProductStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Draft, InReview, Active or Archived.");
    }
}

internal sealed class ListProductsQueryHandler(UPBazaarDbContext dbContext, IInventoryService inventory)
    : IQueryHandler<ListProductsQuery, PagedList<ProductSummaryDto>>
{
    public async Task<Result<PagedList<ProductSummaryDto>>> HandleAsync(
        ListProductsQuery query,
        CancellationToken cancellationToken)
    {
        var products = dbContext.Set<Product>().AsNoTracking();

        if (!query.IncludeUnpublished)
        {
            products = products.Where(p => p.Status == ProductStatus.Active);
        }
        else if (Enum.TryParse<ProductStatus>(query.Status, ignoreCase: true, out var status))
        {
            products = products.Where(p => p.Status == status);
        }

        products = ProductFilters.Apply(products, query.Search, query.CategoryId, query.SellerId);

        var totalCount = await products.CountAsync(cancellationToken);

        var page = await products
            .OrderBy(p => p.Sku)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new { p.PublicId, p.Sku, p.Name, p.Price, p.Currency, p.Status })
            .ToListAsync(cancellationToken);

        // One call for the whole page rather than one per card.
        var stock = await inventory.GetStockLevelsAsync([.. page.Select(p => p.PublicId)], cancellationToken);

        return new PagedList<ProductSummaryDto>(
            [
                .. page.Select(p => new ProductSummaryDto(
                    p.PublicId,
                    p.Sku,
                    p.Name,
                    p.Price,
                    p.Currency,
                    p.Status.ToString(),
                    Math.Max(0, stock[p.PublicId].AvailableQuantity)))
            ],
            query.Page,
            query.PageSize,
            totalCount);
    }
}

/// <summary>
/// One product. As with the list, <paramref name="IncludeUnpublished"/> comes from the caller's
/// permissions: to a shopper a draft does not exist, so it answers 404 rather than 403.
/// </summary>
public sealed record GetProductQuery(Guid ProductId, bool IncludeUnpublished) : IQuery<ProductDto>;

internal sealed class GetProductQueryHandler(UPBazaarDbContext dbContext, IInventoryService inventory)
    : IQueryHandler<GetProductQuery, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(
        GetProductQuery query,
        CancellationToken cancellationToken)
    {
        var product = await ProductLoader.Query(dbContext)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PublicId == query.ProductId, cancellationToken);

        if (product is null || (!query.IncludeUnpublished && product.Status != ProductStatus.Active))
        {
            return Result.Failure<ProductDto>(CatalogErrors.ProductNotFound);
        }

        return await product.ToDtoAsync(inventory, cancellationToken);
    }
}

/// <summary>
/// Search, category and seller filters, shared by the shop's list and the staff one so a search
/// finds the same products on both.
/// </summary>
internal static class ProductFilters
{
    public static IQueryable<Product> Apply(
        IQueryable<Product> products,
        string? search,
        Guid? categoryId,
        Guid? sellerId)
    {
        if (categoryId is { } category)
        {
            // A top-level category lists its children's products too, which is what a shopper
            // clicking "Soap" on the rail expects. One level deep is all the tree has today.
            products = products.Where(p =>
                p.Category.PublicId == category
                || (p.Category.Parent != null && p.Category.Parent.PublicId == category));
        }

        if (sellerId is { } seller)
        {
            products = products.Where(p => p.SellerId == seller);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            // Brand and category are searchable because "Goseva" and "diya" are how a shopper
            // thinks about this catalogue, and neither appears in the product's SKU.
            products = products.Where(p =>
                p.Name.Contains(term)
                || p.Sku.Contains(term)
                || (p.Brand != null && p.Brand.Contains(term))
                || p.Category.Name.Contains(term));
        }

        return products;
    }
}

/// <summary>Loads products with what <see cref="CatalogMappings"/> needs.</summary>
internal static class ProductLoader
{
    public static IQueryable<Product> Query(UPBazaarDbContext dbContext) =>
        dbContext.Set<Product>()
            .Include(p => p.Category)
            .ThenInclude(c => c.Parent);
}
