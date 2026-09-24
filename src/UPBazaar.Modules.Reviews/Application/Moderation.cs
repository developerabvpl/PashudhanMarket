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
/// Reviews for staff. Pending ones oldest first, since each is a buyer waiting to see their
/// words go up; the rest newest first.
/// </summary>
/// <param name="Status">None, Pending, Approved or Rejected. All when null.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Reviews per page.</param>
public sealed record ListReviewsQuery(string? Status, int Page, int PageSize) : IQuery<PagedList<ReviewDto>>;

internal sealed class ListReviewsQueryValidator : AbstractValidator<ListReviewsQuery>
{
    public ListReviewsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<ContentStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be None, Pending, Approved or Rejected.");
    }
}

internal sealed class ListReviewsQueryHandler(UPBazaarDbContext dbContext, PhotoLinks links)
    : IQueryHandler<ListReviewsQuery, PagedList<ReviewDto>>
{
    public async Task<Result<PagedList<ReviewDto>>> HandleAsync(ListReviewsQuery query, CancellationToken cancellationToken)
    {
        var reviews = dbContext.Set<Review>().AsNoTracking();

        var filtered = Enum.TryParse<ContentStatus>(query.Status, ignoreCase: true, out var status);

        if (filtered)
        {
            reviews = reviews.Where(r => r.ContentStatus == status);
        }

        var total = await reviews.CountAsync(cancellationToken);

        var ordered = filtered && status == ContentStatus.Pending
            ? reviews.OrderBy(r => r.ContentSubmittedAtUtc).ThenBy(r => r.Id)
            : reviews.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id);

        var page = await ordered
            .Include(r => r.Photos)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new PagedList<ReviewDto>(
            [.. page.Select(r => r.ToDto(ReviewAudience.AuthorOrStaff, links))],
            query.Page,
            query.PageSize,
            total);
    }
}

/// <summary>Staff let a review's words and photos onto the storefront.</summary>
public sealed record ApproveReviewCommand(Guid ReviewId) : ICommand<ReviewDto>;

internal sealed class ApproveReviewCommandHandler(UPBazaarDbContext dbContext, ICurrentUser currentUser, PhotoLinks links, IClock clock)
    : ICommandHandler<ApproveReviewCommand, ReviewDto>
{
    public Task<Result<ReviewDto>> HandleAsync(ApproveReviewCommand command, CancellationToken cancellationToken) =>
        StaffChange.ApplyAsync(
            dbContext,
            command.ReviewId,
            review => review.Approve(currentUser.UserId, clock.UtcNow),
            links,
            cancellationToken);
}

/// <summary>
/// Staff keep a review's words and photos off the storefront, saying why so the buyer can fix
/// them. The stars still count.
/// </summary>
public sealed record RejectReviewCommand(Guid ReviewId, string Note) : ICommand<ReviewDto>;

internal sealed class RejectReviewCommandValidator : AbstractValidator<RejectReviewCommand>
{
    public RejectReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId).NotEmpty();
        RuleFor(x => x.Note).NotEmpty().MaximumLength(Review.NoteMaxLength)
            .WithMessage("Say what the buyer should change.");
    }
}

internal sealed class RejectReviewCommandHandler(UPBazaarDbContext dbContext, ICurrentUser currentUser, PhotoLinks links, IClock clock)
    : ICommandHandler<RejectReviewCommand, ReviewDto>
{
    public Task<Result<ReviewDto>> HandleAsync(RejectReviewCommand command, CancellationToken cancellationToken) =>
        StaffChange.ApplyAsync(
            dbContext,
            command.ReviewId,
            review => review.Reject(command.Note, currentUser.UserId, clock.UtcNow),
            links,
            cancellationToken);
}

/// <summary>Staff take a seller's reply off the storefront.</summary>
public sealed record HideReviewReplyCommand(Guid ReviewId) : ICommand<ReviewDto>;

internal sealed class HideReviewReplyCommandHandler(UPBazaarDbContext dbContext, PhotoLinks links)
    : ICommandHandler<HideReviewReplyCommand, ReviewDto>
{
    public Task<Result<ReviewDto>> HandleAsync(HideReviewReplyCommand command, CancellationToken cancellationToken) =>
        StaffChange.ApplyAsync(dbContext, command.ReviewId, review => review.HideReply(), links, cancellationToken);
}

/// <summary>Load, change and save one review on behalf of staff.</summary>
internal static class StaffChange
{
    public static async Task<Result<ReviewDto>> ApplyAsync(
        UPBazaarDbContext dbContext,
        Guid reviewId,
        Func<Review, Result> change,
        PhotoLinks links,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.Set<Review>()
            .Include(r => r.Photos)
            .FirstOrDefaultAsync(r => r.PublicId == reviewId, cancellationToken);

        if (review is null)
        {
            return Result.Failure<ReviewDto>(ReviewErrors.ReviewNotFound);
        }

        var result = change(review);

        if (result.IsFailure)
        {
            return Result.Failure<ReviewDto>(result.Error);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Most likely the buyer changed the review while staff were reading it; what staff
            // approved is no longer what would be shown.
            return Result.Failure<ReviewDto>(ReviewErrors.ConcurrentChange);
        }

        return review.ToDto(ReviewAudience.AuthorOrStaff, links);
    }
}
