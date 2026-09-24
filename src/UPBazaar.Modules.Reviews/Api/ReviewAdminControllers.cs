using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Reviews.Application;
using UPBazaar.Modules.Reviews.Contracts.Dtos;
using UPBazaar.Modules.Reviews.Contracts.Permissions;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Reviews.Api;

/// <summary>Reviews of a seller's products, and their replies. The seller is found from the token.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/seller/reviews")]
[Produces("application/json")]
[Authorize(ReviewsPermissions.SellerReply)]
public sealed class SellerReviewsController(IDispatcher dispatcher, ICurrentUser currentUser, ISellerDirectory sellers)
    : ControllerBase
{
    /// <summary>Lists reviews of the seller's products.</summary>
    [HttpGet]
    [EndpointSummary("My product reviews")]
    [EndpointDescription("Newest first. Words and photos show once staff approve them.")]
    [ProducesResponseType<PagedList<ReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<PagedList<ReviewDto>>> List(
        [FromQuery] SellerReviewListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller(seller => dispatcher.QueryAsync(
            new ListSellerReviewsQuery(seller, request.Page ?? 1, request.PageSize ?? 25, request.Unanswered ?? false),
            cancellationToken));
    }

    /// <summary>Writes or changes the seller's reply to a review.</summary>
    [HttpPut("{reviewId:guid}/reply")]
    [EndpointSummary("Reply to a review")]
    [EndpointDescription("One public reply per review, shown under it at once. Writing again replaces it.")]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ReviewDto>> Reply(Guid reviewId, ReviewReplyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller(seller => dispatcher.SendAsync(new ReplyToReviewCommand(seller, reviewId, request.Text), cancellationToken));
    }

    private async Task<ActionResult<T>> AsSeller<T>(Func<Guid, Task<Result<T>>> action)
    {
        var seller = Guid.TryParse(currentUser.UserId, out var user)
            ? await sellers.GetApprovedSellerIdAsync(user, HttpContext.RequestAborted)
            : null;

        return seller is { } id
            ? (await action(id)).ToActionResult()
            : Result.Failure<T>(SellerAccessErrors.NotAnApprovedSeller).ToActionResult();
    }
}

/// <summary>Moderating reviews: approving buyers' words and photos, and hiding sellers' replies.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/reviews")]
[Produces("application/json")]
public sealed class AdminReviewsController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Lists reviews.</summary>
    [HttpGet]
    [Authorize(ReviewsPermissions.Read)]
    [EndpointSummary("List reviews")]
    [EndpointDescription("Filter to Pending for the queue of words and photos waiting for approval, oldest first.")]
    [ProducesResponseType<PagedList<ReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedList<ReviewDto>>> List(
        [FromQuery] ReviewListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.QueryAsync(
                new ListReviewsQuery(request.Status, request.Page ?? 1, request.PageSize ?? 25),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Approves a review's words and photos.</summary>
    [HttpPost("{reviewId:guid}/approve")]
    [Authorize(ReviewsPermissions.Moderate)]
    [EndpointSummary("Approve a review")]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewDto>> Approve(Guid reviewId, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new ApproveReviewCommand(reviewId), cancellationToken)).ToActionResult();

    /// <summary>Rejects a review's words and photos.</summary>
    [HttpPost("{reviewId:guid}/reject")]
    [Authorize(ReviewsPermissions.Moderate)]
    [EndpointSummary("Reject a review")]
    [EndpointDescription("Keeps the words and photos off the storefront; the stars still count. The note tells the buyer what to change.")]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewDto>> Reject(Guid reviewId, RejectReviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(new RejectReviewCommand(reviewId, request.Note), cancellationToken)).ToActionResult();
    }

    /// <summary>Hides the seller's reply to a review.</summary>
    [HttpPost("{reviewId:guid}/reply/hide")]
    [Authorize(ReviewsPermissions.Moderate)]
    [EndpointSummary("Hide a seller's reply")]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewDto>> HideReply(Guid reviewId, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new HideReviewReplyCommand(reviewId), cancellationToken)).ToActionResult();
}

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 25.</param>
/// <param name="Unanswered">Only reviews without a reply.</param>
public sealed record SellerReviewListRequest(int? Page, int? PageSize, bool? Unanswered);

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 25.</param>
/// <param name="Status">None, Pending, Approved or Rejected. All when omitted.</param>
public sealed record ReviewListRequest(int? Page, int? PageSize, string? Status);

/// <param name="Text">The reply, up to 1000 characters.</param>
public sealed record ReviewReplyRequest(string Text);

/// <param name="Note">What the buyer should change, up to 500 characters.</param>
public sealed record RejectReviewRequest(string Note);
