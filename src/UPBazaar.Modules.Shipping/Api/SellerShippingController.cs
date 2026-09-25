using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.Modules.Shipping.Application;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Contracts.Permissions;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Api;

/// <summary>
/// A seller packing and booking couriers for their own parts of orders, the same flow staff use,
/// and setting where their parcels are collected from. A part that is not the caller's answers
/// as not found.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/seller/shipping")]
[Produces("application/json")]
[Authorize(ShippingPermissions.OwnShipmentsWrite)]
public sealed class SellerShippingController(
    IDispatcher dispatcher,
    ICurrentUser currentUser,
    ISellerDirectory sellers,
    IOrderFulfilmentService orders) : ControllerBase
{
    /// <summary>Suggests a parcel for one of the seller's parts.</summary>
    [HttpGet("orders/{orderId:guid}/parts/{partId:guid}/parcel")]
    [EndpointSummary("Suggest my parcel")]
    [ProducesResponseType<ParcelSuggestionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ParcelSuggestionDto>> SuggestParcel(Guid orderId, Guid partId, CancellationToken cancellationToken) =>
        ForOwnPart(orderId, partId, _ => dispatcher.QueryAsync(new GetParcelSuggestionQuery(orderId, partId), cancellationToken), cancellationToken);

    /// <summary>Packs one of the seller's parts and books the courier.</summary>
    [HttpPost("orders/{orderId:guid}/parts/{partId:guid}/pack")]
    [EndpointSummary("Pack and book my parcel")]
    [EndpointDescription("Books the courier and marks the part Packed. Sending it again after a failed step carries on from that step.")]
    [ProducesResponseType<ShipmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ShipmentDto>> Pack(Guid orderId, Guid partId, PackPartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ForOwnPart(
            orderId,
            partId,
            _ => dispatcher.SendAsync(new PackPartCommand(orderId, partId, request.Parcel), cancellationToken),
            cancellationToken);
    }

    /// <summary>Books the pickup for a buyer's approved return of the seller's parcel.</summary>
    [HttpPost("orders/{orderId:guid}/parts/{partId:guid}/return-pickup")]
    [EndpointSummary("Book my return pickup")]
    [EndpointDescription("Approving a return books its pickup automatically; this tries again if that failed.")]
    [ProducesResponseType<ShipmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ShipmentDto>> BookReturnPickup(Guid orderId, Guid partId, CancellationToken cancellationToken) =>
        ForOwnPart(
            orderId,
            partId,
            seller => dispatcher.SendAsync(new BookReturnPickupCommand(orderId, partId, seller), cancellationToken),
            cancellationToken);

    /// <summary>Lists the seller's shipments for one order.</summary>
    [HttpGet("orders/{orderId:guid}/shipments")]
    [EndpointSummary("My shipments for an order")]
    [ProducesResponseType<IReadOnlyList<ShipmentDto>>(StatusCodes.Status200OK)]
    public Task<ActionResult<IReadOnlyList<ShipmentDto>>> Shipments(Guid orderId, CancellationToken cancellationToken) =>
        AsSeller<IReadOnlyList<ShipmentDto>>(async seller =>
        {
            var all = await dispatcher.QueryAsync(new GetOrderShipmentsQuery(orderId, BuyerId: null), cancellationToken);

            return all.IsFailure
                ? all
                : Result.Success<IReadOnlyList<ShipmentDto>>([.. all.Value.Where(s => s.SellerId == seller)]);
        }, cancellationToken);

    /// <summary>Returns where the seller's parcels are collected from.</summary>
    [HttpGet("pickup-location")]
    [EndpointSummary("My pickup location")]
    [EndpointDescription(
        "The seller's own pickup location, or the platform warehouse when they have none. No content "
        + "when neither is set up - not an error, just nothing yet.")]
    [ProducesResponseType<PickupLocationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<ActionResult<PickupLocationDto?>> PickupLocation(CancellationToken cancellationToken) =>
        AsSeller<PickupLocationDto?>(async seller =>
        {
            var all = await dispatcher.QueryAsync(new ListPickupLocationsQuery(), cancellationToken);

            return Result.Success(all.Value.FirstOrDefault(l => l.SellerId == seller) ?? all.Value.FirstOrDefault(l => l.SellerId is null));
        }, cancellationToken);

    /// <summary>Sets the seller's own pickup location.</summary>
    [HttpPut("pickup-location")]
    [EndpointSummary("Set my pickup location")]
    [EndpointDescription("The name of a pickup location registered for the shop in the Shiprocket dashboard.")]
    [ProducesResponseType<PickupLocationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<PickupLocationDto>> SetPickupLocation(SellerPickupLocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller(seller => dispatcher.SendAsync(new SetPickupLocationCommand(seller, request.Name, request.Pincode), cancellationToken), cancellationToken);
    }

    private async Task<ActionResult<T>> ForOwnPart<T>(
        Guid orderId,
        Guid partId,
        Func<Guid, Task<Result<T>>> action,
        CancellationToken cancellationToken) =>
        await AsSeller(async seller =>
        {
            var part = await orders.GetPartAsync(orderId, partId, cancellationToken);

            return part.IsFailure || part.Value.SellerId != seller
                ? Result.Failure<T>(ShippingErrors.PartNotFound)
                : await action(seller);
        }, cancellationToken);

    private async Task<ActionResult<T>> AsSeller<T>(Func<Guid, Task<Result<T>>> action, CancellationToken cancellationToken)
    {
        var seller = Guid.TryParse(currentUser.UserId, out var user)
            ? await sellers.GetApprovedSellerIdAsync(user, cancellationToken)
            : null;

        return seller is { } id
            ? (await action(id)).ToActionResult()
            : Result.Failure<T>(SellerAccessErrors.NotAnApprovedSeller).ToActionResult();
    }
}

/// <param name="Name">The pickup location's name exactly as registered in Shiprocket.</param>
/// <param name="Pincode">Its 6-digit PIN code, so parcels can be priced when booked.</param>
public sealed record SellerPickupLocationRequest(string Name, string? Pincode = null);
