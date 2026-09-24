using UPBazaar.Modules.Reviews.Contracts.Dtos;
using UPBazaar.Modules.Reviews.Domain;
using UPBazaar.Modules.Reviews.Photos;

namespace UPBazaar.Modules.Reviews.Application;

/// <summary>Who is looking at a review, which decides how much of it they see.</summary>
internal enum ReviewAudience
{
    /// <summary>The buyer who wrote it, or staff: everything, with signed links to photos not yet public.</summary>
    AuthorOrStaff,

    /// <summary>The seller: stars and reply always, words and photos only once approved.</summary>
    Seller,
}

internal static class ReviewMappings
{
    public static ReviewDto ToDto(this Review review, ReviewAudience audience, PhotoLinks links)
    {
        var approved = review.ContentStatus == ContentStatus.Approved;
        var showContent = approved || audience == ReviewAudience.AuthorOrStaff;

        return new ReviewDto(
            review.PublicId,
            review.ProductId,
            review.ProductName,
            review.SellerId,
            review.ReviewerName,
            review.Rating,
            showContent ? review.Title : null,
            showContent ? review.Body : null,
            review.ContentStatus.ToString(),
            audience == ReviewAudience.AuthorOrStaff ? review.ModerationNote : null,
            showContent ? review.PhotoDtos(links) : [],
            review.ReplyDto(),
            review.CreatedAtUtc,
            review.ContentSubmittedAtUtc);
    }

    /// <summary>What shoppers see: stars always, words and photos once approved, the reply unless hidden.</summary>
    public static PublicReviewDto ToPublicDto(this Review review, PhotoLinks links)
    {
        var approved = review.ContentStatus == ContentStatus.Approved;

        return new PublicReviewDto(
            review.PublicId,
            review.ReviewerName,
            review.Rating,
            approved ? review.Title : null,
            approved ? review.Body : null,
            approved ? review.PhotoDtos(links) : [],
            review.ReplyHidden ? null : review.ReplyDto(),
            review.CreatedAtUtc);
    }

    private static List<ReviewPhotoDto> PhotoDtos(this Review review, PhotoLinks links)
    {
        var approved = review.ContentStatus == ContentStatus.Approved;

        return
        [
            .. review.Photos
                .OrderBy(p => p.Id)
                .Select(p => new ReviewPhotoDto(p.PublicId, approved ? PhotoLinks.Public(p.PublicId) : links.Private(p.PublicId))),
        ];
    }

    private static ReviewReplyDto? ReplyDto(this Review review) =>
        review is { ReplyText: { } text, RepliedAtUtc: { } at }
            ? new ReviewReplyDto(text, at, review.ReplyHidden)
            : null;
}
