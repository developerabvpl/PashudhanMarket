using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Reviews.Domain;

/// <summary>Every failure this module can return.</summary>
public static class ReviewErrors
{
    public static readonly Error NotSignedIn = Error.Unauthorized(
        "reviews.not_signed_in",
        "Sign in to review what you bought.");

    public static readonly Error NotDelivered = Error.Forbidden(
        "reviews.not_delivered",
        "You can review a product once it has been delivered to you.");

    public static readonly Error ReviewNotFound = Error.NotFound(
        "reviews.review.not_found",
        "Review not found.");

    public static readonly Error PhotoNotFound = Error.NotFound(
        "reviews.photo.not_found",
        "Photo not found.");

    public static readonly Error TooManyPhotos = Error.Validation(
        "reviews.photo.too_many",
        "A review can have at most three photos. Remove one to add another.");

    public static readonly Error PhotoNotAnImage = Error.Validation(
        "reviews.photo.not_an_image",
        "Choose a JPEG, PNG or WebP photo.");

    public static readonly Error PhotoTooLarge = Error.Validation(
        "reviews.photo.too_large",
        "That photo is too large. Choose one under 5 MB.");

    public static readonly Error PhotosUnavailable = Error.Conflict(
        "reviews.photo.unavailable",
        "Photos cannot be added to reviews yet.");

    public static readonly Error NothingToModerate = Error.Conflict(
        "reviews.nothing_to_moderate",
        "This review has nothing waiting for approval. It may have been decided already.");

    public static readonly Error NoReply = Error.Conflict(
        "reviews.reply.none",
        "This review has no reply to hide.");

    public static readonly Error ConcurrentChange = Error.Conflict(
        "reviews.concurrent_change",
        "This was changed by someone else at the same time. Reload and try again.");
}
