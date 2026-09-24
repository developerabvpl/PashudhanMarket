using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Reviews.Domain;
using UPBazaar.Modules.Reviews.Photos;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Reviews.Application;

/// <summary>A photo's bytes, ready to send.</summary>
/// <param name="Content">The file; the caller disposes it.</param>
/// <param name="ContentType">Its image type.</param>
/// <param name="IsPublic">On an approved review, so anyone and any cache may keep it.</param>
public sealed record ReviewPhotoFile(Stream Content, string ContentType, bool IsPublic);

/// <summary>
/// Opens a photo: freely when its review is approved, otherwise only with a valid signed link.
/// Either failure reads as "not found", so a private photo's existence is not given away.
/// </summary>
public sealed record GetReviewPhotoQuery(Guid PhotoId, long? Expires, string? Signature) : IQuery<ReviewPhotoFile>;

internal sealed class GetReviewPhotoQueryHandler(UPBazaarDbContext dbContext, IPhotoStore store, PhotoLinks links)
    : IQueryHandler<GetReviewPhotoQuery, ReviewPhotoFile>
{
    public async Task<Result<ReviewPhotoFile>> HandleAsync(GetReviewPhotoQuery query, CancellationToken cancellationToken)
    {
        var photo = await (
                from p in dbContext.Set<ReviewPhoto>().AsNoTracking()
                join r in dbContext.Set<Review>() on p.ReviewId equals r.Id
                where p.PublicId == query.PhotoId
                select new { p.StorageKey, p.ContentType, r.ContentStatus })
            .FirstOrDefaultAsync(cancellationToken);

        if (photo is null)
        {
            return Result.Failure<ReviewPhotoFile>(ReviewErrors.PhotoNotFound);
        }

        var isPublic = photo.ContentStatus == ContentStatus.Approved;

        if (!isPublic && !links.IsValid(query.PhotoId, query.Expires, query.Signature))
        {
            return Result.Failure<ReviewPhotoFile>(ReviewErrors.PhotoNotFound);
        }

        var content = await store.OpenAsync(photo.StorageKey, cancellationToken);

        return content is null
            ? Result.Failure<ReviewPhotoFile>(ReviewErrors.PhotoNotFound)
            : new ReviewPhotoFile(content, photo.ContentType, isPublic);
    }
}
