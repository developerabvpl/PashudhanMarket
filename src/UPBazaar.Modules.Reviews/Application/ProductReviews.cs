using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Reviews.Contracts.Dtos;
using UPBazaar.Modules.Reviews.Domain;
using UPBazaar.Modules.Reviews.Photos;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Reviews.Application;

/// <summary>
/// A product's rating and a page of its reviews, for shoppers.
///
/// Reviews with approved words come first, newest first, because a page of bare star ratings
/// tells a shopper little; the stars-only ones follow. Every review counts in the rating.
/// </summary>
public sealed record GetProductReviewsQuery(Guid ProductId, int Page, int PageSize) : IQuery<ProductReviewsDto>;

internal sealed class GetProductReviewsQueryValidator : AbstractValidator<GetProductReviewsQuery>
{
    public GetProductReviewsQueryValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

internal sealed class GetProductReviewsQueryHandler(UPBazaarDbContext dbContext, PhotoLinks links)
    : IQueryHandler<GetProductReviewsQuery, ProductReviewsDto>
{
    public async Task<Result<ProductReviewsDto>> HandleAsync(GetProductReviewsQuery query, CancellationToken cancellationToken)
    {
        var reviews = dbContext.Set<Review>().AsNoTracking().Where(r => r.ProductId == query.ProductId);

        var counts = await reviews
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var page = await reviews
            .Include(r => r.Photos)
            .OrderByDescending(r => r.ContentStatus == ContentStatus.Approved)
            .ThenByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var summary = Summarise(counts.ToDictionary(c => c.Rating, c => c.Count));

        return new ProductReviewsDto(
            query.ProductId,
            summary,
            [.. page.Select(r => r.ToPublicDto(links))],
            query.Page,
            query.PageSize,
            summary.Count);
    }

    internal static RatingSummaryDto Summarise(IReadOnlyDictionary<int, int> countsByRating)
    {
        int[] stars = [.. Enumerable.Range(1, 5).Select(r => countsByRating.GetValueOrDefault(r))];
        var count = stars.Sum();

        var average = count == 0
            ? 0m
            : Math.Round((decimal)stars.Select((n, i) => n * (i + 1)).Sum() / count, 1, MidpointRounding.AwayFromZero);

        return new RatingSummaryDto(average, count, stars);
    }
}
