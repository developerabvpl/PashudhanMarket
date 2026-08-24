namespace UPBazaar.SharedKernel.Primitives;

/// <summary>
/// Base for every persisted entity: a bigint identity key for storage and a stable
/// <see cref="PublicId"/> for anything that crosses a module or API boundary.
/// </summary>
public abstract class Entity
{
    /// <summary>Clustered identity key. Never leaves the owning module.</summary>
    public long Id { get; protected set; }

    /// <summary>
    /// The identifier APIs and other modules use. Version 7 so it sorts by creation time and
    /// does not fragment the index the way a random GUID would.
    /// </summary>
    public Guid PublicId { get; protected set; } = Guid.CreateVersion7();
}
