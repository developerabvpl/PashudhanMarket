using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Ordering.Application.Orders;
using UPBazaar.Modules.Ordering.Contracts.Dtos;
using UPBazaar.Modules.Ordering.Contracts.Permissions;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Ordering.Api;

[ApiController]
[Route("api/ordering/orders")]
[Produces("application/json")]
public sealed class OrdersController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Header carrying the client-generated key that makes checkout retry-safe.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    [HttpGet("{orderId:guid}", Name = OrderingRoutes.GetOrder)]
    [Authorize(OrderingPermissions.OrdersRead)]
    [EndpointSummary("Get an order")]
    [EndpointDescription("Returns one order with its priced lines and payment reference.")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> Get(Guid orderId, CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetOrderQuery(orderId), cancellationToken)).ToActionResult();

    [HttpPost("checkout")]
    [Authorize(OrderingPermissions.OrdersWrite)]
    [EndpointSummary("Check out a cart")]
    [EndpointDescription(
        "Reserves stock, places the order and opens a payment. Send an Idempotency-Key header; "
        + "retrying with the same key replays the original order instead of placing another.")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> Checkout(
        CheckoutRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = new CheckoutCommand(
            request.CustomerId,
            request.Lines,
            request.DeliveryPostcode,
            request.SellerId,
            string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.CreateVersion7().ToString() : idempotencyKey);

        return (await dispatcher.SendAsync(command, cancellationToken))
            .ToCreatedResult(OrderingRoutes.GetOrder, order => new { orderId = order.Id });
    }

    [HttpPost("{orderId:guid}/cancel")]
    [Authorize(OrderingPermissions.OrdersCancel)]
    [EndpointSummary("Cancel an order")]
    [EndpointDescription("Cancels an unpaid order and releases the stock it was holding.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Cancel(
        Guid orderId,
        CancelOrderRequest request,
        CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new CancelOrderCommand(orderId, request.Reason), cancellationToken))
        .ToActionResult();
}

public sealed record CheckoutRequest(
    Guid CustomerId,
    Guid SellerId,
    string DeliveryPostcode,
    IReadOnlyList<CheckoutLine> Lines);

public sealed record CancelOrderRequest(string Reason);

public static class OrderingRoutes
{
    public const string GetOrder = "Ordering_GetOrder";
}
