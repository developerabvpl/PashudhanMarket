using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Outbox;
using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Turns domain events raised on aggregates into outbox rows, inside the same transaction as
/// the state change.
///
/// That atomicity is the whole point: an event can never be published for a change that
/// rolled back, and a process that dies after committing has still recorded the work, so the
/// processor picks it up on the next pass.
/// </summary>
public sealed class OutboxInterceptor(ICorrelationContext correlation) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Drain(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Drain(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Drain(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var aggregates = context.ChangeTracker.Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        if (aggregates.Count == 0)
        {
            return;
        }

        var correlationId = correlation.CorrelationId;
        var messages = new List<OutboxMessage>();

        foreach (var aggregate in aggregates)
        {
            var module = context.Model.FindEntityType(aggregate.GetType())?.GetSchema();

            foreach (var domainEvent in aggregate.DomainEvents)
            {
                messages.Add(new OutboxMessage
                {
                    EventId = domainEvent.EventId,
                    Type = OutboxSerializer.TypeName(domainEvent.GetType()),
                    Payload = JsonSerializer.Serialize(
                        domainEvent,
                        domainEvent.GetType(),
                        OutboxSerializer.Options),
                    Module = module,
                    CorrelationId = correlationId,
                    OccurredAtUtc = domainEvent.OccurredAtUtc,
                });
            }

            // Cleared only after the rows exist, so a failure here leaves the events pending
            // on the aggregate rather than silently dropping them.
            aggregate.ClearDomainEvents();
        }

        context.Set<OutboxMessage>().AddRange(messages);
    }
}

/// <summary>Shared JSON settings and type naming for outbox payloads.</summary>
public static class OutboxSerializer
{
    /// <summary>Web defaults, so payloads read the same as the API's JSON.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Type name plus assembly name and nothing else. Including the version would orphan
    /// every pending message the first time the assembly version changes.
    /// </summary>
    public static string TypeName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return $"{type.FullName}, {type.Assembly.GetName().Name}";
    }
}
