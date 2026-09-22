using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Orders.Contracts.Permissions;
using UPBazaar.Modules.Shipping.Application;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Contracts.Permissions;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Api;

/// <summary>A buyer following their own parcels.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/shipping")]
[Produces("application/json")]
public sealed class ShippingController(IDispatcher dispatcher, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Lists the parcels for one of the caller's orders.</summary>
    [HttpGet("orders/{orderId:guid}/shipments")]
    [Authorize(OrdersPermissions.OwnRead)]
    [EndpointSummary("Track my order")]
    [EndpointDescription(
        "One shipment per seller's parcel, with courier, AWB, a tracking link and the courier's "
        + "updates. Empty until something is booked, and for an order that is not the caller's.")]
    [ProducesResponseType<IReadOnlyList<ShipmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ShipmentDto>>> Track(Guid orderId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUser.UserId, out var buyerId))
        {
            return Result.Failure<IReadOnlyList<ShipmentDto>>(ShippingErrors.NotSignedIn).ToActionResult();
        }

        return (await dispatcher.QueryAsync(new GetOrderShipmentsQuery(orderId, buyerId), cancellationToken))
            .ToActionResult();
    }
}

/// <summary>
/// Shiprocket's tracking webhook. Anonymous because Shiprocket cannot sign in; authenticated by
/// the token set on the webhook in the Shiprocket dashboard, sent back as <c>x-api-key</c>.
///
/// The path deliberately avoids the carrier's name: Shiprocket refuses webhook URLs that contain
/// "shiprocket" or a few similar words.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/shipping/webhooks/courier-tracking")]
public sealed class CourierWebhookController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Receives a courier status update.</summary>
    [HttpPost]
    [AllowAnonymous]
    [EndpointSummary("Courier tracking webhook")]
    [EndpointDescription("Receives Shiprocket tracking updates, authenticated by the x-api-key header.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Receive(
        [FromHeader(Name = "x-api-key")] string? token,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        return (await dispatcher.SendAsync(new HandleCourierWebhookCommand(body, token), cancellationToken))
            .ToActionResult();
    }
}

/// <summary>Staff booking couriers and managing where parcels are collected from.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/shipping")]
[Produces("application/json")]
public sealed class AdminShippingController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Suggests a parcel for a seller's part.</summary>
    [HttpGet("orders/{orderId:guid}/parts/{partId:guid}/parcel")]
    [Authorize(ShippingPermissions.ShipmentsWrite)]
    [EndpointSummary("Suggest a parcel")]
    [EndpointDescription(
        "Works the parcel out from the products' recorded packages, and names the pickup location. "
        + "Lists the SKUs with no package when it cannot.")]
    [ProducesResponseType<ParcelSuggestionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParcelSuggestionDto>> SuggestParcel(
        Guid orderId,
        Guid partId,
        CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetParcelSuggestionQuery(orderId, partId), cancellationToken)).ToActionResult();

    /// <summary>Packs a seller's part and books the courier.</summary>
    [HttpPost("orders/{orderId:guid}/parts/{partId:guid}/pack")]
    [Authorize(ShippingPermissions.ShipmentsWrite)]
    [EndpointSummary("Pack and book a courier")]
    [EndpointDescription(
        "Books the parcel with the carrier - order, AWB, pickup - and marks the part Packed. The "
        + "parcel may be left out when every product has a package recorded. If a booking step "
        + "fails, sending this again carries on from that step.")]
    [ProducesResponseType<ShipmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShipmentDto>> Pack(
        Guid orderId,
        Guid partId,
        PackPartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(new PackPartCommand(orderId, partId, request.Parcel), cancellationToken))
            .ToActionResult();
    }

    /// <summary>Lists the shipments for one order.</summary>
    [HttpGet("orders/{orderId:guid}/shipments")]
    [Authorize(ShippingPermissions.ShipmentsRead)]
    [EndpointSummary("An order's shipments")]
    [ProducesResponseType<IReadOnlyList<ShipmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ShipmentDto>>> OrderShipments(
        Guid orderId,
        CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetOrderShipmentsQuery(orderId, BuyerId: null), cancellationToken)).ToActionResult();

    /// <summary>Lists shipments.</summary>
    [HttpGet("shipments")]
    [Authorize(ShippingPermissions.ShipmentsRead)]
    [EndpointSummary("List shipments")]
    [EndpointDescription("Newest first. Search matches an order number or an AWB.")]
    [ProducesResponseType<PagedList<ShipmentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedList<ShipmentDto>>> List(
        [FromQuery] ListShipmentsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.QueryAsync(
                new ListShipmentsQuery(request.Page ?? 1, request.PageSize ?? 20, request.Status, request.Search),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Lists pickup locations.</summary>
    [HttpGet("pickup-locations")]
    [Authorize(ShippingPermissions.ShipmentsRead)]
    [EndpointSummary("List pickup locations")]
    [EndpointDescription("The platform warehouse (no seller) first, then each seller with a pickup address of their own.")]
    [ProducesResponseType<IReadOnlyList<PickupLocationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PickupLocationDto>>> PickupLocations(CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new ListPickupLocationsQuery(), cancellationToken)).ToActionResult();

    /// <summary>Sets a pickup location.</summary>
    [HttpPut("pickup-locations")]
    [Authorize(ShippingPermissions.ShipmentsWrite)]
    [EndpointSummary("Set a pickup location")]
    [EndpointDescription(
        "Sets a seller's pickup location, or the platform warehouse's when sellerId is null. The name "
        + "must match a pickup location registered in the Shiprocket dashboard exactly.")]
    [ProducesResponseType<PickupLocationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PickupLocationDto>> SetPickupLocation(
        PickupLocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(new SetPickupLocationCommand(request.SellerId, request.Name), cancellationToken))
            .ToActionResult();
    }

    /// <summary>Removes a seller's pickup location.</summary>
    [HttpDelete("pickup-locations/{sellerId:guid}")]
    [Authorize(ShippingPermissions.ShipmentsWrite)]
    [EndpointSummary("Remove a seller's pickup location")]
    [EndpointDescription("The seller then ships from the platform warehouse.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemovePickupLocation(Guid sellerId, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new RemovePickupLocationCommand(sellerId), cancellationToken)).ToActionResult();
}

/// <param name="Parcel">The parcel as packed; leave out to use the products' recorded packages.</param>
public sealed record PackPartRequest(ParcelDto? Parcel);

/// <param name="SellerId">The seller, or null for the platform warehouse.</param>
/// <param name="Name">The pickup location's name exactly as registered in Shiprocket.</param>
public sealed record PickupLocationRequest(Guid? SellerId, string Name);

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 20.</param>
/// <param name="Status">Booking, PickupRequested, InTransit, Delivered, Returned or Cancelled.</param>
/// <param name="Search">Order number or AWB.</param>
public sealed record ListShipmentsRequest(int? Page, int? PageSize, string? Status, string? Search);
