using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Reviews.Application;
using UPBazaar.Modules.Reviews.Contracts.Dtos;
using UPBazaar.Modules.Reviews.Contracts.Permissions;
using UPBazaar.Modules.Reviews.Domain;
using UPBazaar.Modules.Reviews.Photos;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Reviews.Api;

/// <summary>What shoppers read: a product's rating and reviews, and review photos.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reviews")]
[AllowAnonymous]
public sealed class ReviewsController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Returns a product's rating and a page of its reviews.</summary>
    [HttpGet("products/{productId:guid}")]
    [Produces("application/json")]
    [EndpointSummary("Get a product's reviews")]
    [EndpointDescription(
        "The rating counts every review. Reviews come with words first, newest first; their words "
        + "and photos appear once staff approve them.")]
    [ProducesResponseType<ProductReviewsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductReviewsDto>> ForProduct(
        Guid productId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetProductReviewsQuery(productId, page ?? 1, pageSize ?? 10), cancellationToken))
            .ToActionResult();

    /// <summary>Returns a review photo.</summary>
    [HttpGet("photos/{photoId:guid}")]
    [EndpointSummary("Get a review photo")]
    [EndpointDescription(
        "Photos on approved reviews are public. Others need the signed link the API handed out, "
        + "which works for an hour.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Photo(
        Guid photoId,
        [FromQuery] long? expires,
        [FromQuery] string? signature,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetReviewPhotoQuery(photoId, expires, signature), cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure(result.Error).ToActionResult();
        }

        var file = result.Value;

        // A private photo is kept only as long as its link lasts, and never by a shared cache.
        Response.Headers.CacheControl = file.IsPublic
            ? "public, max-age=3600"
            : $"private, max-age={(int)PhotoLinks.Lifetime.TotalSeconds}";
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(file.Content, file.ContentType);
    }
}

/// <summary>A buyer's own reviews of what was delivered to them. The buyer is found from the token.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reviews/mine")]
[Produces("application/json")]
[Authorize(ReviewsPermissions.OwnWrite)]
public sealed class MyReviewsController(IDispatcher dispatcher, ICurrentUser currentUser)
    : ControllerBase
{
    /// <summary>Photo upload cap: the largest photo plus room for the multipart wrapping.</summary>
    private const long UploadLimit = PhotoFormat.MaxBytes + (64 * 1024);

    /// <summary>Returns whether the buyer can review a product, and their review.</summary>
    [HttpGet("{productId:guid}")]
    [EndpointSummary("Get my review of a product")]
    [ProducesResponseType<MyReviewDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<MyReviewDto>> Get(Guid productId, CancellationToken cancellationToken) =>
        AsBuyer(buyer => dispatcher.QueryAsync(new GetMyReviewQuery(buyer, productId), cancellationToken));

    /// <summary>Rates a product, or changes the rating or words.</summary>
    [HttpPut("{productId:guid}")]
    [EndpointSummary("Save my review of a product")]
    [EndpointDescription(
        "For a product delivered to you, even if you sent it back. The stars count at once; words "
        + "go to staff first, and changing them sends them back.")]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ReviewDto>> Save(Guid productId, SaveReviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsBuyer(buyer => dispatcher.SendAsync(
            new SaveMyReviewCommand(buyer, currentUser.UserName ?? string.Empty, productId, request.Rating, request.Title, request.Body),
            cancellationToken));
    }

    /// <summary>Adds a photo to the buyer's review.</summary>
    [HttpPost("{productId:guid}/photos")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    [EndpointSummary("Add a photo to my review")]
    [EndpointDescription("A JPEG, PNG or WebP under 5 MB; up to three per review. Staff approve it before it is shown.")]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ReviewDto>> AddPhoto(Guid productId, IFormFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        return AsBuyer(async buyer =>
        {
            await using var content = file.OpenReadStream();

            return await dispatcher.SendAsync(new AddReviewPhotoCommand(buyer, productId, content, file.Length), cancellationToken);
        });
    }

    /// <summary>Takes a photo off the buyer's review.</summary>
    [HttpDelete("{productId:guid}/photos/{photoId:guid}")]
    [EndpointSummary("Remove a photo from my review")]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ReviewDto>> RemovePhoto(Guid productId, Guid photoId, CancellationToken cancellationToken) =>
        AsBuyer(buyer => dispatcher.SendAsync(new RemoveReviewPhotoCommand(buyer, productId, photoId), cancellationToken));

    private async Task<ActionResult<T>> AsBuyer<T>(Func<Guid, Task<Result<T>>> action) =>
        Guid.TryParse(currentUser.UserId, out var buyer)
            ? (await action(buyer)).ToActionResult()
            : Result.Failure<T>(ReviewErrors.NotSignedIn).ToActionResult();
}

/// <param name="Rating">1 to 5 stars.</param>
/// <param name="Title">Optional headline, up to 120 characters.</param>
/// <param name="Body">Optional words, up to 2000 characters.</param>
public sealed record SaveReviewRequest(int Rating, string? Title, string? Body);
