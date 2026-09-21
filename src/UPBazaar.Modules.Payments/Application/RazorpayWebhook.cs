using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.Modules.Payments.Gateway;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Application;

/// <summary>A webhook delivery exactly as received: the raw body is what the signature covers.</summary>
public sealed record HandleRazorpayWebhookCommand(string RawBody, string? Signature, string? EventId) : ICommand;

/// <summary>
/// Razorpay telling us about a payment, whether or not the buyer's browser ever did.
///
/// This is the safety net for a buyer who pays and then loses signal before the page reports it:
/// the money is captured, and without this the order would be cancelled at its deadline with the
/// money still taken. <c>payment.captured</c> and <c>order.paid</c> settle the payment through the
/// same path the browser uses; <c>payment.failed</c> is recorded for support. Anything else is
/// acknowledged and ignored, so enabling another event in the dashboard cannot cause retries.
///
/// A delivery that fails here is answered with an error, and Razorpay retries it. One that
/// succeeds is recorded by event id, so Razorpay's occasional duplicate is skipped.
/// </summary>
internal sealed partial class HandleRazorpayWebhookCommandHandler(
    UPBazaarDbContext dbContext,
    IPaymentGateway gateway,
    PaymentSettler settler,
    IClock clock,
    ILogger<HandleRazorpayWebhookCommandHandler> logger) : ICommandHandler<HandleRazorpayWebhookCommand>
{
    public async Task<Result> HandleAsync(HandleRazorpayWebhookCommand command, CancellationToken cancellationToken)
    {
        if (command.Signature is null || !gateway.IsWebhookSignatureValid(command.RawBody, command.Signature))
        {
            return Result.Failure(PaymentErrors.InvalidSignature);
        }

        WebhookEvent? webhook;

        try
        {
            webhook = WebhookEvent.Parse(command.RawBody);
        }
        catch (JsonException)
        {
            // Signed but unreadable: retrying will not help, so acknowledge it and say so in the log.
            LogUnreadable(logger);

            return Result.Success();
        }

        if (webhook is null)
        {
            return Result.Success();
        }

        var eventId = command.EventId ?? $"{webhook.Type}:{webhook.GatewayPaymentId}";

        if (await dbContext.Set<ProcessedWebhookEvent>().AnyAsync(e => e.EventId == eventId, cancellationToken))
        {
            return Result.Success();
        }

        var handled = await HandleAsync(webhook, cancellationToken);

        if (handled.IsFailure)
        {
            return handled;
        }

        dbContext.ChangeTracker.Clear();
        dbContext.Set<ProcessedWebhookEvent>().Add(ProcessedWebhookEvent.Create(eventId, webhook.Type, clock.UtcNow));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A duplicate delivered at the same moment recorded it first. Handling is idempotent,
            // so both having run is harmless.
        }

        return Result.Success();
    }

    private async Task<Result> HandleAsync(WebhookEvent webhook, CancellationToken cancellationToken)
    {
        switch (webhook.Type)
        {
            case "payment.captured" or "order.paid" when webhook.GatewayPaymentId is not null:
            {
                var settled = await settler.SettleAsync(
                    webhook.GatewayOrderId, webhook.GatewayPaymentId, buyerId: null, cancellationToken);

                // A payment for an order we never created - another integration on the same
                // Razorpay account - is not ours to settle, and retrying will not change that.
                if (settled.IsFailure && settled.Error == PaymentErrors.NotFound)
                {
                    LogUnknownOrder(logger, webhook.GatewayOrderId);

                    return Result.Success();
                }

                return settled.IsSuccess ? Result.Success() : Result.Failure(settled.Error);
            }

            case "payment.failed":
            {
                var payment = await dbContext.Set<Payment>()
                    .FirstOrDefaultAsync(p => p.GatewayOrderId == webhook.GatewayOrderId, cancellationToken);

                if (payment is not null)
                {
                    payment.RecordFailedAttempt(webhook.FailureReason ?? "Payment failed.");
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                return Result.Success();
            }

            default:
                return Result.Success();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored a signed Razorpay webhook whose body could not be read")]
    private static partial void LogUnreadable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Razorpay webhook for unknown gateway order {GatewayOrderId}")]
    private static partial void LogUnknownOrder(ILogger logger, string gatewayOrderId);
}

/// <summary>
/// The few fields of a Razorpay webhook this module reads. Read by hand from the JSON rather than
/// bound to classes, because Razorpay's payloads are large and this needs four values from them.
/// </summary>
internal sealed record WebhookEvent(string Type, string GatewayOrderId, string? GatewayPaymentId, string? FailureReason)
{
    /// <summary>The event, or null when it carries no payment for an order (and so is nothing to us).</summary>
    public static WebhookEvent? Parse(string rawBody)
    {
        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;

        var type = root.TryGetProperty("event", out var eventName) ? eventName.GetString() : null;

        if (type is null
            || !root.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("payment", out var payment)
            || !payment.TryGetProperty("entity", out var entity))
        {
            return null;
        }

        var orderId = String(entity, "order_id");

        return orderId is null
            ? null
            : new WebhookEvent(type, orderId, String(entity, "id"), String(entity, "error_description"));
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
