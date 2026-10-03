using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Payments.Application;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Contracts.Permissions;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.Modules.Payments.Gateway;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Api;

/// <summary>
/// A buyer paying for their own orders. The buyer is always the caller; a payment or order that
/// belongs to someone else answers as not found.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
[Produces("application/json")]
public sealed class PaymentsController(IDispatcher dispatcher, ICurrentUser currentUser, IPaymentGateway gateway)
    : ControllerBase
{
    /// <summary>Says whether online payment is available.</summary>
    [HttpGet("config")]
    [AllowAnonymous]
    [EndpointSummary("Payment options")]
    [EndpointDescription("Whether checkout may offer online payment, and through which gateway.")]
    [ProducesResponseType<PaymentsConfigDto>(StatusCodes.Status200OK)]
    public ActionResult<PaymentsConfigDto> Config() => Ok(new PaymentsConfigDto(gateway.IsEnabled, gateway.Name));

    /// <summary>Starts paying for an order.</summary>
    [HttpPost("orders/{orderId:guid}/checkout")]
    [Authorize(PaymentsPermissions.OwnWrite)]
    [EndpointSummary("Start a payment")]
    [EndpointDescription(
        "Creates, or reuses, the Razorpay order for one of the caller's unpaid online orders and "
        + "returns what Razorpay Checkout needs to open.")]
    [ProducesResponseType<CheckoutSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<CheckoutSessionDto>> Start(Guid orderId, CancellationToken cancellationToken) =>
        AsBuyer(buyerId => dispatcher.SendAsync(new StartPaymentCommand(buyerId, orderId), cancellationToken));

    /// <summary>Reports a completed Razorpay payment.</summary>
    [HttpPost("razorpay/verify")]
    [Authorize(PaymentsPermissions.OwnWrite)]
    [EndpointSummary("Verify a payment")]
    [EndpointDescription(
        "Takes the three values Razorpay Checkout returns on success, checks the signature, and "
        + "confirms the order. The outcome is Confirmed, RefundDue (the order could no longer take "
        + "the money) or Processing.")]
    [ProducesResponseType<PaymentResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<PaymentResultDto>> Verify(
        VerifyRazorpayPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsBuyer(buyerId => dispatcher.SendAsync(
            new VerifyPaymentCommand(buyerId, request.GatewayOrderId, request.GatewayPaymentId, request.Signature),
            cancellationToken));
    }

    /// <summary>Simulates a successful payment. Development only.</summary>
    [HttpPost("fake/{gatewayOrderId}/pay")]
    [Authorize(PaymentsPermissions.OwnWrite)]
    [EndpointSummary("Simulate a payment (development)")]
    [EndpointDescription("Pays a fake gateway order as if Razorpay Checkout had succeeded. Not found unless the fake gateway is in use.")]
    [ProducesResponseType<PaymentResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<PaymentResultDto>> Simulate(string gatewayOrderId, CancellationToken cancellationToken) =>
        AsBuyer(buyerId => dispatcher.SendAsync(new SimulatePaymentCommand(buyerId, gatewayOrderId), cancellationToken));

    private async Task<ActionResult<T>> AsBuyer<T>(Func<Guid, Task<Result<T>>> action)
    {
        if (!Guid.TryParse(currentUser.UserId, out var buyerId))
        {
            return Result.Failure<T>(PaymentErrors.NotSignedIn).ToActionResult();
        }

        return (await action(buyerId)).ToActionResult();
    }
}

/// <summary>
/// Razorpay's webhook. Anonymous because Razorpay cannot sign in; authenticated instead by the
/// HMAC signature over the raw body, which is why the body is read as text rather than bound.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments/webhooks/razorpay")]
public sealed class RazorpayWebhookController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Receives a Razorpay event.</summary>
    [HttpPost]
    [AllowAnonymous]
    [EndpointSummary("Razorpay webhook")]
    [EndpointDescription(
        "Receives payment events from Razorpay. Authenticated by the X-Razorpay-Signature HMAC over "
        + "the raw body and de-duplicated on X-Razorpay-Event-Id, so redelivery is safe.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Receive(
        [FromHeader(Name = "X-Razorpay-Signature")] string? signature,
        [FromHeader(Name = "X-Razorpay-Event-Id")] string? eventId,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        return (await dispatcher.SendAsync(new HandleRazorpayWebhookCommand(body, signature, eventId), cancellationToken))
            .ToActionResult();
    }
}

/// <summary>Staff views of payments and refunds.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/payments")]
[Produces("application/json")]
public sealed class AdminPaymentsController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Lists payments.</summary>
    [HttpGet]
    [Authorize(PaymentsPermissions.Read)]
    [EndpointSummary("List payments")]
    [EndpointDescription("Newest first. Search matches an order number, a Razorpay order id or a Razorpay payment id.")]
    [ProducesResponseType<PagedList<PaymentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedList<PaymentDto>>> List(
        [FromQuery] ListPaymentsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.QueryAsync(
                new ListPaymentsQuery(request.Page ?? 1, request.PageSize ?? 20, request.Status, request.Search),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Lists refunds.</summary>
    [HttpGet("refunds")]
    [Authorize(PaymentsPermissions.Read)]
    [EndpointSummary("List refunds")]
    [EndpointDescription("Filter to Due for the refunds still to make, oldest first.")]
    [ProducesResponseType<PagedList<RefundDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedList<RefundDto>>> ListRefunds(
        [FromQuery] ListRefundsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.QueryAsync(
                new ListRefundsQuery(request.Page ?? 1, request.PageSize ?? 20, request.Status),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Records a refund as made.</summary>
    [HttpPost("refunds/{refundId:guid}/mark-refunded")]
    [Authorize(PaymentsPermissions.RefundsWrite)]
    [EndpointSummary("Record a refund as made")]
    [EndpointDescription("After refunding in the Razorpay dashboard, records the Razorpay refund id against the refund.")]
    [ProducesResponseType<RefundDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RefundDto>> MarkRefunded(
        Guid refundId,
        MarkRefundedRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(new MarkRefundedCommand(refundId, request.GatewayRefundId), cancellationToken))
            .ToActionResult();
    }
}

/// <param name="GatewayOrderId">Checkout's <c>razorpay_order_id</c>.</param>
/// <param name="GatewayPaymentId">Checkout's <c>razorpay_payment_id</c>.</param>
/// <param name="Signature">Checkout's <c>razorpay_signature</c>.</param>
public sealed record VerifyRazorpayPaymentRequest(string GatewayOrderId, string GatewayPaymentId, string Signature);

/// <param name="GatewayRefundId">
/// The refund id Razorpay showed, <c>rfnd_...</c>; or for a UPI refund, the UPI transaction reference (UTR).
/// </param>
public sealed record MarkRefundedRequest(string GatewayRefundId);

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 20.</param>
/// <param name="Status">Created, Paid or Abandoned.</param>
/// <param name="Search">Order number, or a Razorpay order or payment id.</param>
public sealed record ListPaymentsRequest(int? Page, int? PageSize, string? Status, string? Search);

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 20.</param>
/// <param name="Status">Due or Refunded.</param>
public sealed record ListRefundsRequest(int? Page, int? PageSize, string? Status);
