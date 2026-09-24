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

/// <summary>Whether a buyer can review a product, with their review if they wrote one.</summary>
public sealed record GetMyReviewQuery(Guid BuyerId, Guid ProductId) : IQuery<MyReviewDto>;

internal sealed class GetMyReviewQueryHandler(UPBazaarDbContext dbContext, IPhotoStore photos, PhotoLinks links)
    : IQueryHandler<GetMyReviewQuery, MyReviewDto>
{
    public async Task<Result<MyReviewDto>> HandleAsync(GetMyReviewQuery query, CancellationToken cancellationToken)
    {
        var canReview = await dbContext.Set<ReviewableLine>()
            .AnyAsync(l => l.BuyerId == query.BuyerId && l.ProductId == query.ProductId, cancellationToken);

        var review = await dbContext.Set<Review>()
            .AsNoTracking()
            .Include(r => r.Photos)
            .FirstOrDefaultAsync(r => r.BuyerId == query.BuyerId && r.ProductId == query.ProductId, cancellationToken);

        return new MyReviewDto(
            query.ProductId,
            canReview,
            photos.IsEnabled,
            Review.MaxPhotos,
            review?.ToDto(ReviewAudience.AuthorOrStaff, links));
    }
}

/// <summary>A buyer rates a product delivered to them, or changes their rating or words.</summary>
/// <param name="BuyerId">The signed-in buyer.</param>
/// <param name="ReviewerName">Their display name, shown with the review.</param>
/// <param name="ProductId">The product.</param>
/// <param name="Rating">1 to 5 stars.</param>
/// <param name="Title">Optional headline.</param>
/// <param name="Body">Optional words.</param>
public sealed record SaveMyReviewCommand(
    Guid BuyerId,
    string ReviewerName,
    Guid ProductId,
    int Rating,
    string? Title,
    string? Body) : ICommand<ReviewDto>;

internal sealed class SaveMyReviewCommandValidator : AbstractValidator<SaveMyReviewCommand>
{
    public SaveMyReviewCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween(1, 5).WithMessage("Choose from one to five stars.");
        RuleFor(x => x.Title).MaximumLength(Review.TitleMaxLength);
        RuleFor(x => x.Body).MaximumLength(Review.BodyMaxLength);
    }
}

internal sealed class SaveMyReviewCommandHandler(UPBazaarDbContext dbContext, PhotoLinks links, IClock clock)
    : ICommandHandler<SaveMyReviewCommand, ReviewDto>
{
    public async Task<Result<ReviewDto>> HandleAsync(SaveMyReviewCommand command, CancellationToken cancellationToken)
    {
        var review = await BuyerReview.FindAsync(dbContext, command.BuyerId, command.ProductId, cancellationToken);
        var name = BuyerReview.NameOf(command.ReviewerName);

        if (review is null)
        {
            var line = await dbContext.Set<ReviewableLine>()
                .Where(l => l.BuyerId == command.BuyerId && l.ProductId == command.ProductId)
                .OrderByDescending(l => l.DeliveredAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (line is null)
            {
                return Result.Failure<ReviewDto>(ReviewErrors.NotDelivered);
            }

            review = Review.Create(line, name, command.Rating, command.Title, command.Body, clock.UtcNow);
            dbContext.Set<Review>().Add(review);
        }
        else
        {
            review.Edit(name, command.Rating, command.Title, command.Body, clock.UtcNow);
        }

        return await BuyerReview.SaveAsync(dbContext, review, links, cancellationToken);
    }
}

/// <summary>A buyer adds a photo to their review.</summary>
/// <param name="BuyerId">The signed-in buyer.</param>
/// <param name="ProductId">The product their review is of.</param>
/// <param name="Content">The uploaded file.</param>
/// <param name="Length">Its size in bytes, as the upload declared it.</param>
public sealed record AddReviewPhotoCommand(Guid BuyerId, Guid ProductId, Stream Content, long Length) : ICommand<ReviewDto>;

internal sealed class AddReviewPhotoCommandHandler(
    UPBazaarDbContext dbContext,
    IPhotoStore store,
    PhotoLinks links,
    IClock clock) : ICommandHandler<AddReviewPhotoCommand, ReviewDto>
{
    public async Task<Result<ReviewDto>> HandleAsync(AddReviewPhotoCommand command, CancellationToken cancellationToken)
    {
        if (!store.IsEnabled)
        {
            return Result.Failure<ReviewDto>(ReviewErrors.PhotosUnavailable);
        }

        if (command.Length > PhotoFormat.MaxBytes)
        {
            return Result.Failure<ReviewDto>(ReviewErrors.PhotoTooLarge);
        }

        var review = await BuyerReview.FindAsync(dbContext, command.BuyerId, command.ProductId, cancellationToken);

        if (review is null)
        {
            return Result.Failure<ReviewDto>(ReviewErrors.ReviewNotFound);
        }

        if (review.Photos.Count >= Review.MaxPhotos)
        {
            return Result.Failure<ReviewDto>(ReviewErrors.TooManyPhotos);
        }

        // Read whole - the endpoint caps the request size - and measured again, because the
        // declared length is only the client's word, and the type has to be judged from the
        // bytes before anything is stored.
        using var buffer = new MemoryStream();
        await command.Content.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length > PhotoFormat.MaxBytes)
        {
            return Result.Failure<ReviewDto>(ReviewErrors.PhotoTooLarge);
        }

        var contentType = PhotoFormat.Detect(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, PhotoFormat.HeaderLength)));

        if (contentType is null)
        {
            return Result.Failure<ReviewDto>(ReviewErrors.PhotoNotAnImage);
        }

        var key = Guid.CreateVersion7().ToString("N") + PhotoFormat.ExtensionFor(contentType);
        var photo = ReviewPhoto.Create(key, contentType, buffer.Length, clock.UtcNow);

        var added = review.AddPhoto(photo, clock.UtcNow);

        if (added.IsFailure)
        {
            return Result.Failure<ReviewDto>(added.Error);
        }

        // File first, row second: a row without its file would be a broken image forever, while
        // a file without its row is only wasted space, and is removed below when the save fails.
        buffer.Position = 0;
        await store.SaveAsync(key, buffer, cancellationToken);

        var saved = await BuyerReview.SaveAsync(dbContext, review, links, cancellationToken);

        if (saved.IsFailure)
        {
            await store.DeleteAsync(key, CancellationToken.None);
        }

        return saved;
    }
}

/// <summary>A buyer takes a photo off their review.</summary>
public sealed record RemoveReviewPhotoCommand(Guid BuyerId, Guid ProductId, Guid PhotoId) : ICommand<ReviewDto>;

internal sealed class RemoveReviewPhotoCommandHandler(UPBazaarDbContext dbContext, IPhotoStore store, PhotoLinks links)
    : ICommandHandler<RemoveReviewPhotoCommand, ReviewDto>
{
    public async Task<Result<ReviewDto>> HandleAsync(RemoveReviewPhotoCommand command, CancellationToken cancellationToken)
    {
        var review = await BuyerReview.FindAsync(dbContext, command.BuyerId, command.ProductId, cancellationToken);

        if (review is null)
        {
            return Result.Failure<ReviewDto>(ReviewErrors.ReviewNotFound);
        }

        var removed = review.RemovePhoto(command.PhotoId);

        if (removed.IsFailure)
        {
            return Result.Failure<ReviewDto>(removed.Error);
        }

        var saved = await BuyerReview.SaveAsync(dbContext, review, links, cancellationToken);

        if (saved.IsSuccess)
        {
            await store.DeleteAsync(removed.Value.StorageKey, cancellationToken);
        }

        return saved;
    }
}

/// <summary>Loading and saving a buyer's own review, shared by the commands above.</summary>
internal static class BuyerReview
{
    public static Task<Review?> FindAsync(
        UPBazaarDbContext dbContext,
        Guid buyerId,
        Guid productId,
        CancellationToken cancellationToken) =>
        dbContext.Set<Review>()
            .Include(r => r.Photos)
            .FirstOrDefaultAsync(r => r.BuyerId == buyerId && r.ProductId == productId, cancellationToken);

    /// <summary>The name shown with a review; a token without one still gets something readable.</summary>
    public static string NameOf(string? displayName)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? "Buyer" : displayName.Trim();

        return name.Length > 128 ? name[..128] : name;
    }

    public static async Task<Result<ReviewDto>> SaveAsync(
        UPBazaarDbContext dbContext,
        Review review,
        PhotoLinks links,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<ReviewDto>(ReviewErrors.ConcurrentChange);
        }

        return review.ToDto(ReviewAudience.AuthorOrStaff, links);
    }
}
