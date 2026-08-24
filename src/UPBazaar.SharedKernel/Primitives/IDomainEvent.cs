namespace UPBazaar.SharedKernel.Primitives;

/// <summary>
/// A fact that has already happened inside a module. Domain events are captured on the
/// aggregate, written to the shared outbox in the same transaction as the state change,
/// and dispatched asynchronously - including to handlers in other modules.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    DateTime OccurredAtUtc { get; }
}

/// <summary>Convenience base record for domain events.</summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
}
