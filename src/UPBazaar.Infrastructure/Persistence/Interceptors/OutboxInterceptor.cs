using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using UPBazaar.Infrastructure.Persistence.Outbox;
using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Converts domain events raised on aggregates into outbox rows inside the same transaction
/// as the state change, so an event can never be published for a change that rolled back.
/// </summary>
public sealed class OutboxInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Drain(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Drain(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Drain(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var aggregates = context.ChangeTracker.Entries<Entity>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        if (aggregates.Count == 0)
        {
            return;
        }

        var messages = new List<OutboxMessage>();

        foreach (var aggregate in aggregates)
        {
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
                    OccurredAtUtc = domainEvent.OccurredAtUtc,
                });
            }

            aggregate.ClearDomainEvents();
        }

        context.Set<OutboxMessage>().AddRange(messages);
    }
}

/// <summary>Shared JSON settings and type naming for outbox payloads.</summary>
public static class OutboxSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Type name without assembly version, so a rebuild does not orphan pending messages.
    /// </summary>
    public static string TypeName(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";
}
