using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Shipping.Application.Shipments;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Contracts.Permissions;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Shipping.Api;

[ApiController]
[Route("api/shipping/shipments")]
[Produces("application/json")]
public sealed class ShipmentsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("{shipmentId:guid}", Name = ShippingRoutes.GetShipment)]
    [Authorize(ShippingPermissions.ShipmentsRead)]
    [EndpointSummary("Get a shipment")]
    [EndpointDescription("Returns one shipment with its AWB number and current status.")]
    [ProducesResponseType<ShipmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShipmentDto>> Get(Guid shipmentId, CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetShipmentQuery(shipmentId), cancellationToken)).ToActionResult();

    [HttpGet("by-order/{orderId:guid}")]
    [Authorize(ShippingPermissions.ShipmentsRead)]
    [EndpointSummary("Get the shipment for an order")]
    [EndpointDescription("Looks up the consignment booked for a given order.")]
    [ProducesResponseType<ShipmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShipmentDto>> GetByOrder(
        Guid orderId,
        CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetShipmentByOrderQuery(orderId), cancellationToken))
        .ToActionResult();
}

public static class ShippingRoutes
{
    public const string GetShipment = "Shipping_GetShipment";
}
