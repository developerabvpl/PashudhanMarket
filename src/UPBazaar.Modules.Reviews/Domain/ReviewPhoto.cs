using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Reviews.Domain;

/// <summary>
/// A photo on a review. The file lives in the photo store under <see cref="StorageKey"/>; this
/// row is what says whose it is and whether anyone may see it yet.
/// </summary>
public sealed class ReviewPhoto : Entity
{
    private ReviewPhoto()
    {
    }

    public long ReviewId { get; private set; }

    /// <summary>The file's name in the photo store. Never shown: photos are fetched by public id.</summary>
    public string StorageKey { get; private set; } = string.Empty;

    /// <summary>image/jpeg, image/png or image/webp, as read from the file's own bytes.</summary>
    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public DateTime UploadedAtUtc { get; private set; }

    public static ReviewPhoto Create(string storageKey, string contentType, long sizeBytes, DateTime now) =>
        new()
        {
            StorageKey = storageKey,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            UploadedAtUtc = now,
        };
}
