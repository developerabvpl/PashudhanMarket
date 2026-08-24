using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Infrastructure.Persistence.Interceptors;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Outbox;
using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Infrastructure.Outbox;

/// <summary>
/// Drains the shared outbox on a Hangfire recurring job and hands each event to its handlers.
///
/// This is the skeleton: it reads a batch oldest-first, dispatches, and records the outcome
/// per message so one poison event cannot block the queue. What it does not yet do is claim
/// rows for a specific worker, so a multi-instance deployment needs either the disable-
/// concurrent-execution filter below to remain in force or a row-level claim added here.
/// </summary>
public sealed class OutboxProcessor(
    UPBazaarDbContext dbContext,
    IDispatcher dispatcher,
    IClock clock,
    ILogger<OutboxProcessor> logger)
{
    /// <summary>Recurring job id, so the schedule can be updated rather than duplicated.</summary>
    public const string RecurringJobId = "outbox-processor";

    private const int BatchSize = 50;

    /// <summary>After this many failures a message is parked for a human to look at.</summary>
    private const int MaxAttempts = 5;

    /// <summary>Processes one batch. Safe to run more often than the batch takes.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of messages processed successfully.</returns>
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task<int> ProcessAsync(CancellationToken cancellationToken = default)
    {
        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.OccurredAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return 0;
        }

        var processed = 0;

        foreach (var message in messages)
        {
            message.Attempts++;

            try
            {
                await DispatchAsync(message, cancellationToken);

                message.ProcessedAtUtc = clock.UtcNow;
                message.Error = null;
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Recorded rather than rethrown: one bad message must not abandon the batch.
                message.Error = ex.Message;

                logger.LogError(
                    ex,
                    "Outbox message {EventId} of type {EventType} failed on attempt {Attempt} of {MaxAttempts}",
                    message.EventId,
                    message.Type,
                    message.Attempts,
                    MaxAttempts);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return processed;
    }

    private async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var eventType = Type.GetType(message.Type)
            ?? throw new InvalidOperationException(
                $"Cannot resolve outbox event type '{message.Type}'. The declaring assembly may have "
                + "been renamed or removed while messages were still pending.");

        if (JsonSerializer.Deserialize(message.Payload, eventType, OutboxSerializer.Options)
            is not IDomainEvent domainEvent)
        {
            throw new InvalidOperationException($"Outbox payload for '{message.Type}' is not a domain event.");
        }

        await dispatcher.PublishAsync(domainEvent, cancellationToken);
    }
}
