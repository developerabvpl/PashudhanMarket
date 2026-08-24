using System.Reflection;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Infrastructure.Persistence.Interceptors;
using UPBazaar.Infrastructure.Persistence.Outbox;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Infrastructure.Messaging;

/// <summary>
/// Drains the shared outbox on a Hangfire recurring job. Handlers may live in any module,
/// which is how a state change in one module produces an effect in another.
/// </summary>
public sealed class OutboxProcessor(
    UPBazaarDbContext dbContext,
    IServiceProvider provider,
    IClock clock,
    ILogger<OutboxProcessor> logger)
{
    public const string RecurringJobId = "outbox-processor";

    private const int BatchSize = 50;
    private const int MaxAttempts = 5;

    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.OccurredAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            message.Attempts++;

            try
            {
                await DispatchAsync(message, cancellationToken);
                message.ProcessedAtUtc = clock.UtcNow;
                message.Error = null;
            }
            catch (Exception ex)
            {
                message.Error = ex.Message;

                logger.LogError(
                    ex,
                    "Outbox message {EventId} of type {EventType} failed on attempt {Attempt}",
                    message.EventId,
                    message.Type,
                    message.Attempts);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var eventType = Type.GetType(message.Type)
            ?? throw new InvalidOperationException($"Cannot resolve outbox event type '{message.Type}'.");

        if (JsonSerializer.Deserialize(message.Payload, eventType, OutboxSerializer.Options)
            is not IDomainEvent domainEvent)
        {
            throw new InvalidOperationException($"Outbox payload for '{message.Type}' is not a domain event.");
        }

        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var handlers = provider.GetServices(handlerType).Where(h => h is not null).ToList();

        if (handlers.Count == 0)
        {
            logger.LogDebug("No handler registered for domain event {EventType}", eventType.Name);
            return;
        }

        var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;

        foreach (var handler in handlers)
        {
            await (Task)method.Invoke(handler, [domainEvent, cancellationToken])!;
        }
    }
}
