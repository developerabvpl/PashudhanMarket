using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.ExternalServices.Payments;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Infrastructure.Persistence.Idempotency;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Application.Webhooks;

/// <param name="RawPayload">Exact bytes received; the signature is computed over this, not over a re-serialised object.</param>
public sealed record HandleGatewayWebhookCommand(
    string EventId,
    string RawPayload,
    string? Signature) : ICommand;

internal sealed class HandleGatewayWebhookCommandValidator
    : AbstractValidator<HandleGatewayWebhookCommand>
{
    public HandleGatewayWebhookCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.RawPayload).NotEmpty();
    }
}

/// <summary>
/// Gateways retry aggressively and deliver out of order, so this verifies the signature first
/// and then runs under the gateway event id as an idempotency key.
/// </summary>
internal sealed class HandleGatewayWebhookCommandHandler(
    UPBazaarDbContext dbContext,
    IPaymentGateway gateway,
    IIdempotencyService idempotency,
    IClock clock,
    ILogger<HandleGatewayWebhookCommandHandler> logger) : ICommandHandler<HandleGatewayWebhookCommand>
{
    private const string Endpoint = "payments.webhook";

    public async Task<Result> HandleAsync(
        HandleGatewayWebhookCommand command,
        CancellationToken cancellationToken)
    {
        if (!gateway.VerifyWebhookSignature(command.RawPayload, command.Signature))
        {
            logger.LogWarning("Rejected webhook {EventId}: signature did not verify", command.EventId);
            return Result.Failure(PaymentErrors.InvalidSignature);
        }

        var key = $"{Endpoint}:{command.EventId}";
        var reservation = await idempotency.TryBeginAsync(
            key,
            Endpoint,
            IdempotencyService.Hash(command.RawPayload),
            cancellationToken);

        if (reservation.IsConflict)
        {
            return Result.Failure(PaymentErrors.IdempotencyConflict);
        }

        if (reservation.IsReplay)
        {
            // Already handled; acknowledging again is the correct answer to a redelivery.
            return Result.Success();
        }

        if (!GatewayWebhookPayload.TryParse(command.RawPayload, out var payload))
        {
            logger.LogWarning("Webhook {EventId} had an unrecognised payload shape", command.EventId);
            return Result.Failure(Error.Validation(
                "payments.webhook.unreadable",
                "The webhook payload could not be read."));
        }

        var payment = await dbContext.Set<Payment>()
            .FirstOrDefaultAsync(p => p.GatewayOrderId == payload.GatewayOrderId, cancellationToken);

        if (payment is null)
        {
            logger.LogWarning(
                "Webhook {EventId} referenced unknown gateway order {GatewayOrderId}",
                command.EventId,
                payload.GatewayOrderId);

            return Result.Failure(PaymentErrors.NotFound);
        }

        var outcome = payload.EventType switch
        {
            "payment.captured" => payment.Capture(payload.GatewayPaymentId ?? string.Empty, clock.UtcNow),
            "payment.failed" => payment.Fail(payload.Reason ?? "Reported failed by gateway."),
            _ => Result.Success(),
        };

        if (outcome.IsFailure)
        {
            return outcome;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(PaymentErrors.ConcurrencyConflict);
        }

        await idempotency.CompleteAsync(key, payload.EventType, cancellationToken);

        return Result.Success();
    }
}

/// <summary>The handful of fields we need out of a Razorpay-shaped webhook body.</summary>
internal sealed record GatewayWebhookPayload(
    string EventType,
    string GatewayOrderId,
    string? GatewayPaymentId,
    string? Reason)
{
    public static bool TryParse(string rawPayload, out GatewayWebhookPayload payload)
    {
        payload = null!;

        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var root = document.RootElement;

            if (!root.TryGetProperty("event", out var eventType))
            {
                return false;
            }

            var entity = root
                .GetProperty("payload")
                .GetProperty("payment")
                .GetProperty("entity");

            if (!entity.TryGetProperty("order_id", out var orderId))
            {
                return false;
            }

            payload = new GatewayWebhookPayload(
                eventType.GetString() ?? string.Empty,
                orderId.GetString() ?? string.Empty,
                entity.TryGetProperty("id", out var paymentId) ? paymentId.GetString() : null,
                entity.TryGetProperty("error_description", out var reason) ? reason.GetString() : null);

            return !string.IsNullOrWhiteSpace(payload.GatewayOrderId);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }
}
