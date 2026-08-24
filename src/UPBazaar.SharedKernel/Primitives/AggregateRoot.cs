namespace UPBazaar.SharedKernel.Primitives;

/// <summary>
/// An entity that is the transactional boundary for its cluster of objects, and the only
/// place domain events may be raised.
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Events raised since the aggregate was loaded, drained by the outbox interceptor.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Called by the persistence layer once the events have been written to the outbox.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
