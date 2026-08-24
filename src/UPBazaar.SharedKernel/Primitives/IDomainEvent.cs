namespace UPBazaar.SharedKernel.Primitives;

/// <summary>
/// Something that has already happened inside a module. Domain events are raised on the
/// aggregate, persisted to the outbox in the same transaction as the state change, and
/// dispatched afterwards - including to handlers that live in other modules.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Stable identity of this occurrence, used to de-duplicate delivery.</summary>
    Guid EventId { get; }

    DateTime OccurredAtUtc { get; }
}

/// <summary>Convenience base for domain events.</summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
}
