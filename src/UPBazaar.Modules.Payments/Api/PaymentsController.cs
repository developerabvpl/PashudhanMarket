using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Payments.Application.Payments;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Contracts.Permissions;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Payments.Api;

[ApiController]
[Route("api/payments")]
[Produces("application/json")]
public sealed class PaymentsController(IDispatcher dispatcher) : ControllerBase
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    [HttpGet("{paymentId:guid}", Name = PaymentsRoutes.GetPayment)]
    [Authorize(PaymentsPermissions.PaymentsRead)]
    [EndpointSummary("Get a payment")]
    [EndpointDescription("Returns one payment with its current gateway status.")]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentDto>> Get(Guid paymentId, CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetPaymentQuery(paymentId), cancellationToken)).ToActionResult();

    [HttpPost("{paymentId:guid}/refunds")]
    [Authorize(PaymentsPermissions.RefundsWrite)]
    [EndpointSummary("Refund a payment")]
    [EndpointDescription(
        "Refunds part or all of a captured payment and reverses the pending settlement. "
        + "Send an Idempotency-Key header; a retry with the same key replays the first refund.")]
    [ProducesResponseType<RefundDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RefundDto>> Refund(
        Guid paymentId,
        RefundRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = new RefundPaymentCommand(
            paymentId,
            request.Amount,
            request.Reason,
            string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.CreateVersion7().ToString() : idempotencyKey);

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }
}

public sealed record RefundRequest(decimal Amount, string Reason);

public static class PaymentsRoutes
{
    public const string GetPayment = "Payments_GetPayment";
}
