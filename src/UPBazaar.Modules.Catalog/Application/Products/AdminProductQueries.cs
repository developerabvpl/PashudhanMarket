using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;
using UPBazaar.Modules.Inventory.Contracts;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Application.Products;

/// <summary>
/// A page of the catalogue as staff work through it: every status, newest change first, or
/// oldest first when <paramref name="OldestFirst"/> is set - how a review queue should be worked,
/// so the seller who has waited longest is answered first.
/// </summary>
public sealed record ListAdminProductsQuery(
    int Page,
    int PageSize,
    string? Search,
    Guid? CategoryId,
    string? Status,
    Guid? SellerId,
    bool OldestFirst) : IQuery<PagedList<AdminProductSummaryDto>>;

internal sealed class ListAdminProductsQueryValidator : AbstractValidator<ListAdminProductsQuery>
{
    public ListAdminProductsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(256);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<ProductStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Draft, InReview, Active or Archived.");
    }
}

internal sealed class ListAdminProductsQueryHandler(
    UPBazaarDbContext dbContext,
    IInventoryService inventory,
    ISellerDirectory sellers)
    : IQueryHandler<ListAdminProductsQuery, PagedList<AdminProductSummaryDto>>
{
    public async Task<Result<PagedList<AdminProductSummaryDto>>> HandleAsync(
        ListAdminProductsQuery query,
        CancellationToken cancellationToken)
    {
        var products = ProductFilters.Apply(
            dbContext.Set<Product>().AsNoTracking(),
            query.Search,
            query.CategoryId,
            query.SellerId);

        if (Enum.TryParse<ProductStatus>(query.Status, ignoreCase: true, out var status))
        {
            products = products.Where(p => p.Status == status);
        }

        var totalCount = await products.CountAsync(cancellationToken);

        var ordered = query.OldestFirst
            ? products.OrderBy(p => p.ModifiedAtUtc ?? p.CreatedAtUtc).ThenBy(p => p.Id)
            : products.OrderByDescending(p => p.ModifiedAtUtc ?? p.CreatedAtUtc).ThenByDescending(p => p.Id);

        var page = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new
            {
                p.PublicId,
                p.Sku,
                p.Name,
                p.Price,
                p.Currency,
                p.Status,
                CategoryId = p.Category.PublicId,
                CategoryName = p.Category.Name,
                p.SellerId,
                HasPackage = p.WeightGrams != null,
                UpdatedAtUtc = p.ModifiedAtUtc ?? p.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        // One call each for the whole page: stock from Inventory, shop names from Sellers.
        var stock = await inventory.GetStockLevelsAsync([.. page.Select(p => p.PublicId)], cancellationToken);
        var names = await sellers.GetShopNamesAsync([.. page.Select(p => p.SellerId)], cancellationToken);

        return new PagedList<AdminProductSummaryDto>(
            [
                .. page.Select(p => new AdminProductSummaryDto(
                    p.PublicId,
                    p.Sku,
                    p.Name,
                    p.Price,
                    p.Currency,
                    p.Status.ToString(),
                    p.CategoryId,
                    p.CategoryName,
                    p.SellerId,
                    names.GetValueOrDefault(p.SellerId),
                    Math.Max(0, stock[p.PublicId].AvailableQuantity),
                    p.HasPackage,
                    p.UpdatedAtUtc))
            ],
            query.Page,
            query.PageSize,
            totalCount);
    }
}
