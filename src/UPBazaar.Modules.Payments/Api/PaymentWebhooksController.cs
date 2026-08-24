using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Payments.Application.Webhooks;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Payments.Api;

[ApiController]
[Route("api/payments/webhooks")]
public sealed class PaymentWebhooksController(IDispatcher dispatcher) : ControllerBase
{
    public const string SignatureHeader = "X-Razorpay-Signature";

    public const string EventIdHeader = "X-Razorpay-Event-Id";

    /// <summary>
    /// Anonymous by design: the gateway cannot present a bearer token, so the HMAC signature
    /// over the raw body is the authentication.
    /// </summary>
    [HttpPost("razorpay")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [EndpointSummary("Razorpay webhook")]
    [EndpointDescription(
        "Receives payment lifecycle events. Authenticated by HMAC signature over the raw body "
        + "and de-duplicated on the gateway event id, so redelivery is safe.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Razorpay(
        [FromHeader(Name = SignatureHeader)] string? signature,
        [FromHeader(Name = EventIdHeader)] string? eventId,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var rawPayload = await reader.ReadToEndAsync(cancellationToken);

        var command = new HandleGatewayWebhookCommand(
            string.IsNullOrWhiteSpace(eventId) ? Guid.CreateVersion7().ToString() : eventId,
            rawPayload,
            signature);

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }
}
