namespace UPBazaar.SharedKernel.Primitives;

/// <summary>
/// Base type for all persisted entities: bigint identity <see cref="Id"/> for storage,
/// <see cref="PublicId"/> for anything crossing a module or API boundary.
/// </summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Clustered identity key. Never exposed outside the owning module.</summary>
    public long Id { get; protected set; }

    /// <summary>Stable public identifier - this is what APIs and other modules use.</summary>
    public Guid PublicId { get; protected set; } = Guid.CreateVersion7();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
