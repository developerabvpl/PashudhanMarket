using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Application.Products;

public sealed record ListProductsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    Guid? CategoryId = null,
    bool ActiveOnly = true) : IQuery<PagedList<ProductSummaryDto>>;

internal sealed class ListProductsQueryValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(200);
    }
}

internal sealed class ListProductsQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListProductsQuery, PagedList<ProductSummaryDto>>
{
    public async Task<Result<PagedList<ProductSummaryDto>>> HandleAsync(
        ListProductsQuery query,
        CancellationToken cancellationToken)
    {
        var products = dbContext.Set<Product>().AsNoTracking();

        if (query.ActiveOnly)
        {
            products = products.Where(p => p.Status == ProductStatus.Active);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            products = products.Where(p => p.Name.Contains(search) || p.Sku.Contains(search));
        }

        if (query.CategoryId is { } categoryId)
        {
            products = products.Where(p => p.Category.PublicId == categoryId);
        }

        var totalCount = await products.CountAsync(cancellationToken);

        var page = await products
            .OrderBy(p => p.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<ProductSummaryDto>(
            page.Select(p => p.ToSummary()).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }
}
