using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Orders.Application;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Contracts.Permissions;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Api;

/// <summary>
/// A seller's order queue: their own parts of orders, found from the caller's token. Packing
/// and booking a courier happen in Shipping, which marks the part Packed.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/seller/orders")]
[Produces("application/json")]
[Authorize(OrdersPermissions.SellerRead)]
public sealed class SellerOrdersController(IDispatcher dispatcher, ICurrentUser currentUser, ISellerDirectory sellers)
    : ControllerBase
{
    /// <summary>Lists the seller's parts of orders.</summary>
    [HttpGet]
    [EndpointSummary("List my orders to fulfil")]
    [EndpointDescription(
        "The seller's parts of paid or cash-on-delivery orders. Filter to Confirmed for what to pack next, oldest first.")]
    [ProducesResponseType<PagedList<SellerOrderSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<PagedList<SellerOrderSummaryDto>>> List(
        [FromQuery] SellerOrdersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller(seller => dispatcher.QueryAsync(
            new ListSellerOrdersQuery(seller, request.Page ?? 1, request.PageSize ?? 25, request.Status),
            cancellationToken));
    }

    /// <summary>Returns the seller's part of one order.</summary>
    [HttpGet("{orderId:guid}")]
    [EndpointSummary("Get my part of an order")]
    [EndpointDescription("What to pack and where it goes. Not found for an order with nothing of the caller's in it.")]
    [ProducesResponseType<SellerOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<SellerOrderDto>> Get(Guid orderId, CancellationToken cancellationToken) =>
        AsSeller(seller => dispatcher.QueryAsync(new GetSellerOrderQuery(seller, orderId), cancellationToken));

    /// <summary>Records what the seller found in a returned parcel.</summary>
    [HttpPost("{orderId:guid}/parts/{partId:guid}/return-inspection")]
    [EndpointSummary("Inspect my returned parcel")]
    [EndpointDescription(
        "For a part the courier brought back undelivered: Good puts its stock back on sale, Damaged does not. Once per parcel.")]
    [ProducesResponseType<SellerOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<SellerOrderDto>> InspectReturn(
        Guid orderId,
        Guid partId,
        InspectReturnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller<SellerOrderDto>(async seller =>
        {
            var inspected = await dispatcher.SendAsync(
                new InspectReturnCommand(orderId, partId, seller, request.Condition, request.Note), cancellationToken);

            return inspected.IsFailure
                ? Result.Failure<SellerOrderDto>(inspected.Error)
                : await dispatcher.QueryAsync(new GetSellerOrderQuery(seller, orderId), cancellationToken);
        });
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

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 25.</param>
/// <param name="Status">Confirmed, Packed, Shipped, Delivered, Cancelled, Returning or Returned.</param>
public sealed record SellerOrdersRequest(int? Page, int? PageSize, string? Status);

/// <param name="Condition">Good (restock it) or Damaged (do not).</param>
/// <param name="Note">What was wrong, if anything.</param>
public sealed record InspectReturnRequest(string Condition, string? Note);
