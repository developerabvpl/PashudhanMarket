namespace UPBazaar.Modules.Reviews.Contracts.Dtos;

/// <summary>How a product is rated, counting every review whether or not its text is approved.</summary>
/// <param name="Average">Mean star rating to one decimal; 0 when there are no reviews.</param>
/// <param name="Count">How many buyers rated it.</param>
/// <param name="Stars">How many gave each rating: index 0 is one star, index 4 is five.</param>
public sealed record RatingSummaryDto(decimal Average, int Count, IReadOnlyList<int> Stars);

/// <summary>A photo on a review.</summary>
/// <param name="Id">Public id.</param>
/// <param name="Url">
/// Where to load it. Photos on an approved review have a plain, cacheable address; anything
/// else carries a short-lived signed token, because an image tag cannot send a bearer token.
/// </param>
public sealed record ReviewPhotoDto(Guid Id, string Url);

/// <summary>The seller's answer to a review.</summary>
/// <param name="Text">What they wrote.</param>
/// <param name="RepliedAtUtc">When they last wrote or changed it.</param>
/// <param name="Hidden">Staff have hidden it from the storefront.</param>
public sealed record ReviewReplyDto(string Text, DateTime RepliedAtUtc, bool Hidden);

/// <summary>A review as shoppers see it.</summary>
/// <param name="Id">Public id.</param>
/// <param name="ReviewerName">The buyer's name as it was when they wrote it.</param>
/// <param name="Rating">1 to 5 stars.</param>
/// <param name="Title">Headline; null until staff approve it.</param>
/// <param name="Body">What they wrote; null until staff approve it.</param>
/// <param name="Photos">Photos, once staff approve them.</param>
/// <param name="Reply">The seller's reply, unless hidden.</param>
/// <param name="CreatedAtUtc">When the review was first written.</param>
public sealed record PublicReviewDto(
    Guid Id,
    string ReviewerName,
    int Rating,
    string? Title,
    string? Body,
    IReadOnlyList<ReviewPhotoDto> Photos,
    ReviewReplyDto? Reply,
    DateTime CreatedAtUtc);

/// <summary>A product's rating and one page of its reviews, newest first.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Summary">Its rating.</param>
/// <param name="Items">This page of reviews.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Reviews per page.</param>
/// <param name="TotalCount">Reviews in all.</param>
public sealed record ProductReviewsDto(
    Guid ProductId,
    RatingSummaryDto Summary,
    IReadOnlyList<PublicReviewDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>A review as its author, a seller or staff see it.</summary>
/// <param name="Id">Public id.</param>
/// <param name="ProductId">The product reviewed.</param>
/// <param name="ProductName">Its name when it was delivered.</param>
/// <param name="SellerId">Whose product it is.</param>
/// <param name="ReviewerName">The buyer's name as it was when they wrote it.</param>
/// <param name="Rating">1 to 5 stars. Counts at once, whatever happens to the text.</param>
/// <param name="Title">Headline. Shown to sellers only once approved.</param>
/// <param name="Body">What the buyer wrote. Shown to sellers only once approved.</param>
/// <param name="ContentStatus">
/// None (stars only), Pending (text or photos waiting for staff), Approved or Rejected.
/// </param>
/// <param name="ModerationNote">Why staff rejected the text, for the buyer to fix.</param>
/// <param name="Photos">Up to three photos.</param>
/// <param name="Reply">The seller's reply.</param>
/// <param name="CreatedAtUtc">When the review was first written.</param>
/// <param name="ContentSubmittedAtUtc">When the text or photos last changed; the moderation queue's order.</param>
public sealed record ReviewDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    Guid SellerId,
    string ReviewerName,
    int Rating,
    string? Title,
    string? Body,
    string ContentStatus,
    string? ModerationNote,
    IReadOnlyList<ReviewPhotoDto> Photos,
    ReviewReplyDto? Reply,
    DateTime CreatedAtUtc,
    DateTime? ContentSubmittedAtUtc);

/// <summary>Whether the signed-in buyer can review a product, and their review if they have one.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="CanReview">It was delivered to them, so they may rate it.</param>
/// <param name="PhotosEnabled">Photos can be added. False where no photo store is configured.</param>
/// <param name="MaxPhotos">Most photos one review may carry.</param>
/// <param name="Review">Their review, or null if they have not written one.</param>
public sealed record MyReviewDto(
    Guid ProductId,
    bool CanReview,
    bool PhotosEnabled,
    int MaxPhotos,
    ReviewDto? Review);
