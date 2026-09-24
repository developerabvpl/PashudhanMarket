using UPBazaar.Modules.Reviews.Contracts.Events;
using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Reviews.Domain;

/// <summary>Where a review's words and photos stand with staff.</summary>
public enum ContentStatus
{
    /// <summary>Stars only: nothing for staff to look at.</summary>
    None = 0,

    /// <summary>New or changed text or photos, not yet shown to anyone but the buyer.</summary>
    Pending = 1,

    /// <summary>Shown on the storefront.</summary>
    Approved = 2,

    /// <summary>Kept off the storefront, with a note saying why. The stars still count.</summary>
    Rejected = 3,
}

/// <summary>
/// One buyer's rating of one product, with optional words and photos, and the seller's reply.
///
/// The stars count the moment they are given; only what the buyer wrote or photographed waits
/// for staff. That keeps the rating honest - a one-star review is never held back - while abuse
/// or a phone number in the text never reaches the storefront. Any change to the words or
/// photos sends them back for approval, since an approved review edited into something else
/// must be looked at again.
///
/// One review per buyer and product, however many times they bought it; they edit it rather
/// than add another.
/// </summary>
public sealed class Review : AggregateRoot, IAuditable
{
    /// <summary>Most photos on one review.</summary>
    public const int MaxPhotos = 3;

    public const int TitleMaxLength = 120;

    public const int BodyMaxLength = 2000;

    public const int ReplyMaxLength = 1000;

    public const int NoteMaxLength = 500;

    private readonly List<ReviewPhoto> _photos = [];

    private Review()
    {
    }

    public Guid ProductId { get; private set; }

    public Guid SellerId { get; private set; }

    public string ProductName { get; private set; } = string.Empty;

    public Guid BuyerId { get; private set; }

    /// <summary>The order that delivered the product; its page is where the buyer edits the review.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The buyer's display name when they last saved the review.</summary>
    public string ReviewerName { get; private set; } = string.Empty;

    public int Rating { get; private set; }

    public string? Title { get; private set; }

    public string? Body { get; private set; }

    public ContentStatus ContentStatus { get; private set; }

    /// <summary>When the text or photos were last sent for approval; the moderation queue's order.</summary>
    public DateTime? ContentSubmittedAtUtc { get; private set; }

    /// <summary>Why staff rejected the text, for the buyer to fix.</summary>
    public string? ModerationNote { get; private set; }

    public DateTime? ModeratedAtUtc { get; private set; }

    /// <summary>User id of whoever approved or rejected it.</summary>
    public string? ModeratedBy { get; private set; }

    public string? ReplyText { get; private set; }

    public DateTime? RepliedAtUtc { get; private set; }

    /// <summary>
    /// Staff hid the reply. It stays hidden when the seller edits it, or hiding would last only
    /// until the next edit.
    /// </summary>
    public bool ReplyHidden { get; private set; }

    public IReadOnlyCollection<ReviewPhoto> Photos => _photos.AsReadOnly();

    /// <summary>Optimistic concurrency: an approval must not land on text the buyer has just changed.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public bool HasContent => Title is not null || Body is not null || _photos.Count > 0;

    public static Review Create(
        ReviewableLine line,
        string reviewerName,
        int rating,
        string? title,
        string? body,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(line);

        var review = new Review
        {
            ProductId = line.ProductId,
            SellerId = line.SellerId,
            ProductName = line.ProductName,
            BuyerId = line.BuyerId,
            OrderId = line.OrderId,
        };

        review.Edit(reviewerName, rating, title, body, now);

        review.Raise(new ReviewWrittenDomainEvent(
            review.PublicId, review.SellerId, review.ProductId, review.ProductName, review.Rating, review.HasContent));

        return review;
    }

    /// <summary>Sets the stars and the words. Changed words go back to staff.</summary>
    public void Edit(string reviewerName, int rating, string? title, string? body, DateTime now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rating, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rating, 5);

        ReviewerName = reviewerName;
        Rating = rating;

        title = Clean(title);
        body = Clean(body);

        if (title == Title && body == Body)
        {
            return;
        }

        Title = title;
        Body = body;
        ContentChanged(now);
    }

    public Result AddPhoto(ReviewPhoto photo, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(photo);

        if (_photos.Count >= MaxPhotos)
        {
            return Result.Failure(ReviewErrors.TooManyPhotos);
        }

        _photos.Add(photo);
        ContentChanged(now);

        return Result.Success();
    }

    /// <summary>
    /// Takes a photo off. Nothing new is shown by removing one, so the review keeps its standing
    /// unless nothing is left for staff to judge.
    /// </summary>
    public Result<ReviewPhoto> RemovePhoto(Guid photoId)
    {
        var photo = _photos.FirstOrDefault(p => p.PublicId == photoId);

        if (photo is null)
        {
            return Result.Failure<ReviewPhoto>(ReviewErrors.PhotoNotFound);
        }

        _photos.Remove(photo);

        if (!HasContent)
        {
            ClearModeration();
        }

        return photo;
    }

    public Result Approve(string? by, DateTime now) => Decide(ContentStatus.Approved, note: null, by, now);

    public Result Reject(string note, string? by, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        var decided = Decide(ContentStatus.Rejected, note.Trim(), by, now);

        if (decided.IsSuccess)
        {
            Raise(new ReviewRejectedDomainEvent(PublicId, BuyerId, OrderId, ProductName, ModerationNote!));
        }

        return decided;
    }

    /// <summary>The seller writes or rewrites their one reply.</summary>
    public void Reply(string text, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        ReplyText = text.Trim();
        RepliedAtUtc = now;
    }

    public Result HideReply()
    {
        if (ReplyText is null)
        {
            return Result.Failure(ReviewErrors.NoReply);
        }

        ReplyHidden = true;

        return Result.Success();
    }

    private Result Decide(ContentStatus outcome, string? note, string? by, DateTime now)
    {
        if (ContentStatus != ContentStatus.Pending)
        {
            return Result.Failure(ReviewErrors.NothingToModerate);
        }

        ContentStatus = outcome;
        ModerationNote = note;
        ModeratedAtUtc = now;
        ModeratedBy = by;

        return Result.Success();
    }

    private void ContentChanged(DateTime now)
    {
        if (!HasContent)
        {
            ClearModeration();

            return;
        }

        ContentStatus = ContentStatus.Pending;
        ContentSubmittedAtUtc = now;
        ModerationNote = null;
        ModeratedAtUtc = null;
        ModeratedBy = null;
    }

    private void ClearModeration()
    {
        ContentStatus = ContentStatus.None;
        ContentSubmittedAtUtc = null;
        ModerationNote = null;
        ModeratedAtUtc = null;
        ModeratedBy = null;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
