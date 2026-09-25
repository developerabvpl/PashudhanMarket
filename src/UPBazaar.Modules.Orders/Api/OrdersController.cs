using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Orders.Application;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Contracts.Permissions;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Api;

/// <summary>
/// The signed-in buyer's own orders. The buyer is always the caller, taken from their token; an
/// order id that belongs to somebody else answers exactly like one that does not exist.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders")]
[Produces("application/json")]
public sealed class OrdersController(
    IDispatcher dispatcher,
    ICurrentUser currentUser,
    IOptions<OrdersModuleOptions> options) : ControllerBase
{
    /// <summary>Returns the delivery charge rule.</summary>
    [HttpGet("delivery-charge")]
    [AllowAnonymous]
    [EndpointSummary("Get the delivery charge")]
    [EndpointDescription(
        "The flat charge on an order, and the value of goods from which delivery is free, so the "
        + "cart and checkout can show what the buyer will pay. Checkout applies the same rule.")]
    [ProducesResponseType<DeliveryChargeDto>(StatusCodes.Status200OK)]
    public ActionResult<DeliveryChargeDto> DeliveryCharge()
    {
        var rule = options.Value;

        return Ok(new DeliveryChargeDto(rule.DeliveryFee, rule.FreeDeliveryFrom > 0 ? rule.FreeDeliveryFrom : null, "INR"));
    }

    /// <summary>Places an order from the cart.</summary>
    [HttpPost]
    [Authorize(OrdersPermissions.OwnWrite)]
    [EndpointSummary("Check out")]
    [EndpointDescription(
        "Turns the cart into an order and empties it. The cart must have no problem lines. Cash on "
        + "delivery is confirmed at once; an online order waits for payment and is cancelled if it "
        + "has not arrived within 15 minutes.")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetBuyer(out var buyerId))
        {
            return NotSignedIn<OrderDto>();
        }

        return (await dispatcher.SendAsync(
                new PlaceOrderCommand(buyerId, request.PaymentMethod, request.DeliveryAddress, request.CouponCode),
                cancellationToken))
            .ToCreatedResult(OrdersRoutes.GetMine, order => new { orderId = order.Id });
    }

    /// <summary>Lists the caller's orders.</summary>
    [HttpGet]
    [Authorize(OrdersPermissions.OwnRead)]
    [EndpointSummary("List my orders")]
    [EndpointDescription("The caller's orders, newest first.")]
    [ProducesResponseType<PagedList<OrderSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedList<OrderSummaryDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!TryGetBuyer(out var buyerId))
        {
            return NotSignedIn<PagedList<OrderSummaryDto>>();
        }

        return (await dispatcher.QueryAsync(
                new ListOrdersQuery(page ?? 1, pageSize ?? 20, buyerId, Status: null, Number: null),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Returns one of the caller's orders.</summary>
    [HttpGet("{orderId:guid}", Name = OrdersRoutes.GetMine)]
    [Authorize(OrdersPermissions.OwnRead)]
    [EndpointSummary("Get my order")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> Get(Guid orderId, CancellationToken cancellationToken)
    {
        if (!TryGetBuyer(out var buyerId))
        {
            return NotSignedIn<OrderDto>();
        }

        return (await dispatcher.QueryAsync(new GetOrderQuery(orderId, buyerId), cancellationToken))
            .ToActionResult();
    }

    /// <summary>Cancels one of the caller's orders.</summary>
    [HttpPost("{orderId:guid}/cancel")]
    [Authorize(OrdersPermissions.OwnWrite)]
    [EndpointSummary("Cancel my order")]
    [EndpointDescription(
        "Cancels the whole order and puts its stock back, as long as nothing has shipped. An order "
        + "paid online is refunded by Payments.")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> Cancel(
        Guid orderId,
        CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetBuyer(out var buyerId))
        {
            return NotSignedIn<OrderDto>();
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled by the buyer." : request.Reason;

        return (await dispatcher.SendAsync(new CancelOrderCommand(orderId, buyerId, reason), cancellationToken))
            .ToActionResult();
    }

    /// <summary>Asks to return a delivered part of one of the caller's orders.</summary>
    [HttpPost("{orderId:guid}/parts/{partId:guid}/return")]
    [Authorize(OrdersPermissions.OwnWrite)]
    [EndpointSummary("Ask to return a parcel")]
    [EndpointDescription(
        "For a delivered part, within its return window (see returnableUntilUtc). The whole parcel "
        + "goes back. The seller approves or refuses; once approved a courier collects it, and the "
        + "refund is due when it is back with the seller. A cash-on-delivery order needs a UPI id "
        + "for the refund. Once per parcel.")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> RequestReturn(
        Guid orderId,
        Guid partId,
        RequestReturnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetBuyer(out var buyerId))
        {
            return NotSignedIn<OrderDto>();
        }

        return (await dispatcher.SendAsync(
                new RequestReturnCommand(orderId, partId, buyerId, request.Reason, request.Comment, request.RefundUpiId),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Lists the states and union territories an order can be delivered to.</summary>
    [HttpGet("delivery-states")]
    [AllowAnonymous]
    [EndpointSummary("List delivery states")]
    [EndpointDescription("The spellings checkout accepts for the State field.")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> DeliveryStates() => Ok(IndianStates.All);

    private bool TryGetBuyer(out Guid buyerId) => Guid.TryParse(currentUser.UserId, out buyerId);

    private static ActionResult<T> NotSignedIn<T>() => Result.Failure<T>(OrderErrors.NotSignedIn).ToActionResult();
}

/// <summary>Staff access to every order.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/orders")]
[Produces("application/json")]
public sealed class AdminOrdersController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Lists orders.</summary>
    [HttpGet]
    [Authorize(OrdersPermissions.Read)]
    [EndpointSummary("List orders")]
    [EndpointDescription("Every order, newest first, optionally filtered by status or by part of the order number.")]
    [ProducesResponseType<PagedList<OrderSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedList<OrderSummaryDto>>> List(
        [FromQuery] ListOrdersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.QueryAsync(
                new ListOrdersQuery(request.Page ?? 1, request.PageSize ?? 20, BuyerId: null, request.Status, request.Number),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Returns any order.</summary>
    [HttpGet("{orderId:guid}")]
    [Authorize(OrdersPermissions.Read)]
    [EndpointSummary("Get an order")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> Get(Guid orderId, CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetOrderQuery(orderId, BuyerId: null), cancellationToken)).ToActionResult();

    /// <summary>Cancels an order.</summary>
    [HttpPost("{orderId:guid}/cancel")]
    [Authorize(OrdersPermissions.Cancel)]
    [EndpointSummary("Cancel an order")]
    [EndpointDescription("Cancels everything that has not shipped and puts the stock back. A reason is required.")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> Cancel(
        Guid orderId,
        CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(
                new CancelOrderCommand(orderId, BuyerId: null, request.Reason ?? string.Empty),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Moves a seller's part forward.</summary>
    [HttpPost("{orderId:guid}/parts/{partId:guid}/status")]
    [Authorize(OrdersPermissions.Write)]
    [EndpointSummary("Update a part's status")]
    [EndpointDescription(
        "Moves one seller's part to Packed, Shipped or Delivered. Steps may be skipped but never "
        + "reversed. When every live part is delivered the order is Completed.")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> AdvancePart(
        Guid orderId,
        Guid partId,
        AdvanceOrderPartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(
                new AdvanceOrderPartCommand(orderId, partId, request.Status),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Lists buyers' return requests.</summary>
    [HttpGet("returns")]
    [Authorize(OrdersPermissions.Read)]
    [EndpointSummary("List return requests")]
    [EndpointDescription(
        "Buyers' requests to return delivered parcels, from every seller. Filter to Requested for "
        + "the ones still waiting for a decision, oldest first.")]
    [ProducesResponseType<PagedList<ReturnRequestSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedList<ReturnRequestSummaryDto>>> ListReturns(
        [FromQuery] ReturnRequestsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.QueryAsync(
                new ListReturnRequestsQuery(request.Page ?? 1, request.PageSize ?? 25, request.Status, SellerId: null),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Accepts or refuses a buyer's return on the seller's behalf.</summary>
    [HttpPost("{orderId:guid}/parts/{partId:guid}/return-decision")]
    [Authorize(OrdersPermissions.Write)]
    [EndpointSummary("Decide a return request")]
    [EndpointDescription(
        "For a seller who has not answered, or a dispute. Approving books a courier pickup from the "
        + "buyer; refusing needs a note, which the buyer sees.")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> DecideReturn(
        Guid orderId,
        Guid partId,
        DecideReturnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(
                new DecideReturnCommand(orderId, partId, SellerId: null, request.Approve, request.Note),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Records what was found in a returned parcel.</summary>
    [HttpPost("{orderId:guid}/parts/{partId:guid}/return-inspection")]
    [Authorize(OrdersPermissions.Write)]
    [EndpointSummary("Inspect a returned parcel")]
    [EndpointDescription(
        "For a part that came back, on the seller's behalf: Good puts its stock back on sale, Damaged does not.")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> InspectReturn(
        Guid orderId,
        Guid partId,
        InspectReturnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(
                new InspectReturnCommand(orderId, partId, SellerId: null, request.Condition, request.Note),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Cancels a seller's part.</summary>
    [HttpPost("{orderId:guid}/parts/{partId:guid}/cancel")]
    [Authorize(OrdersPermissions.Cancel)]
    [EndpointSummary("Cancel a part")]
    [EndpointDescription(
        "Cancels one seller's part of a confirmed order, before it ships, and puts its stock back. "
        + "The rest of the order carries on.")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> CancelPart(
        Guid orderId,
        Guid partId,
        CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(
                new CancelOrderPartCommand(orderId, partId, request.Reason ?? string.Empty),
                cancellationToken))
            .ToActionResult();
    }
}

/// <param name="PaymentMethod">CashOnDelivery or Online.</param>
/// <param name="DeliveryAddress">Where the order goes.</param>
/// <param name="CouponCode">A coupon code, if the buyer has one. Priced again now; a coupon that no longer applies fails the checkout.</param>
public sealed record PlaceOrderRequest(string PaymentMethod, DeliveryAddressDto DeliveryAddress, string? CouponCode = null);

/// <param name="Reason">Why. Optional for a buyer; required for staff.</param>
public sealed record CancelOrderRequest(string? Reason);

/// <param name="Status">Packed, Shipped or Delivered.</param>
public sealed record AdvanceOrderPartRequest(string Status);

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 20.</param>
/// <param name="Status">PendingPayment, Confirmed, Completed or Cancelled.</param>
/// <param name="Number">All or part of an order number.</param>
public sealed record ListOrdersRequest(int? Page, int? PageSize, string? Status, string? Number);

/// <param name="Reason">Damaged, WrongItem, NotAsDescribed, QualityIssue, NoLongerNeeded or Other.</param>
/// <param name="Comment">What is wrong, in the buyer's words. Required for Other.</param>
/// <param name="RefundUpiId">Where to send the refund, such as name@okicici. Required for a cash-on-delivery order.</param>
public sealed record RequestReturnRequest(string Reason, string? Comment, string? RefundUpiId);

/// <param name="Approve">True to accept and book a pickup; false to refuse.</param>
/// <param name="Note">Why it is refused, shown to the buyer. Required when refusing.</param>
public sealed record DecideReturnRequest(bool Approve, string? Note);

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 25.</param>
/// <param name="Status">Requested, Approved or Rejected. All when omitted.</param>
public sealed record ReturnRequestsRequest(int? Page, int? PageSize, string? Status);

/// <summary>Route names, for Location headers.</summary>
public static class OrdersRoutes
{
    public const string GetMine = "Orders.GetMine";
}
