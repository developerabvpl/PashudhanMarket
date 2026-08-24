using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Identity.Domain;

/// <summary>
/// One thing a caller may do, named as <c>module.resource.action</c>.
///
/// Rows are seeded from the permission catalogue rather than written by hand, so the database
/// and the code cannot disagree about what exists.
/// </summary>
public sealed class Permission : Entity
{
    private Permission()
    {
    }

    public string Name { get; private set; } = null!;

    /// <summary>Owning module, taken from the first segment of the name.</summary>
    public string Module { get; private set; } = null!;

    public string? Description { get; private set; }

    public static Permission Create(string name, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var separator = name.IndexOf('.', StringComparison.Ordinal);

        return new Permission
        {
            Name = name,
            Module = separator > 0 ? name[..separator] : name,
            Description = description,
        };
    }
}
