using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Reviews.Contracts.Dtos;
using UPBazaar.Modules.Reviews.Domain;
using UPBazaar.Modules.Reviews.Photos;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Reviews.Application;

/// <summary>
/// Reviews of a seller's products, newest first. Words and photos appear once staff approve
/// them: the seller should not see what the storefront would not show.
/// </summary>
/// <param name="SellerId">The seller.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Reviews per page.</param>
/// <param name="Unanswered">Only reviews without a reply.</param>
public sealed record ListSellerReviewsQuery(Guid SellerId, int Page, int PageSize, bool Unanswered)
    : IQuery<PagedList<ReviewDto>>;

internal sealed class ListSellerReviewsQueryValidator : AbstractValidator<ListSellerReviewsQuery>
{
    public ListSellerReviewsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

internal sealed class ListSellerReviewsQueryHandler(UPBazaarDbContext dbContext, PhotoLinks links)
    : IQueryHandler<ListSellerReviewsQuery, PagedList<ReviewDto>>
{
    public async Task<Result<PagedList<ReviewDto>>> HandleAsync(ListSellerReviewsQuery query, CancellationToken cancellationToken)
    {
        var reviews = dbContext.Set<Review>().AsNoTracking().Where(r => r.SellerId == query.SellerId);

        if (query.Unanswered)
        {
            reviews = reviews.Where(r => r.ReplyText == null);
        }

        var total = await reviews.CountAsync(cancellationToken);

        var page = await reviews
            .Include(r => r.Photos)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new PagedList<ReviewDto>(
            [.. page.Select(r => r.ToDto(ReviewAudience.Seller, links))],
            query.Page,
            query.PageSize,
            total);
    }
}

/// <summary>
/// A seller writes, or rewrites, their one public reply to a review. It shows at once; staff can
/// hide it if it crosses a line.
/// </summary>
public sealed record ReplyToReviewCommand(Guid SellerId, Guid ReviewId, string Text) : ICommand<ReviewDto>;

internal sealed class ReplyToReviewCommandValidator : AbstractValidator<ReplyToReviewCommand>
{
    public ReplyToReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId).NotEmpty();
        RuleFor(x => x.Text).NotEmpty().MaximumLength(Review.ReplyMaxLength);
    }
}

internal sealed class ReplyToReviewCommandHandler(UPBazaarDbContext dbContext, PhotoLinks links, IClock clock)
    : ICommandHandler<ReplyToReviewCommand, ReviewDto>
{
    public async Task<Result<ReviewDto>> HandleAsync(ReplyToReviewCommand command, CancellationToken cancellationToken)
    {
        var review = await dbContext.Set<Review>()
            .Include(r => r.Photos)
            .FirstOrDefaultAsync(r => r.PublicId == command.ReviewId, cancellationToken);

        // Another seller's review is reported as missing, not forbidden, so ids cannot be probed.
        if (review is null || review.SellerId != command.SellerId)
        {
            return Result.Failure<ReviewDto>(ReviewErrors.ReviewNotFound);
        }

        review.Reply(command.Text, clock.UtcNow);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<ReviewDto>(ReviewErrors.ConcurrentChange);
        }

        return review.ToDto(ReviewAudience.Seller, links);
    }
}
