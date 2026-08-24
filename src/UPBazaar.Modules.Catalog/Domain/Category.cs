using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Catalog.Domain;

public sealed class Category : Entity, IAuditable
{
    private Category()
    {
    }

    public string Name { get; private set; } = null!;

    public string Slug { get; private set; } = null!;

    public long? ParentId { get; private set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public static Category Create(string name, string slug, long? parentId = null) => new()
    {
        Name = name.Trim(),
        Slug = slug.Trim().ToLowerInvariant(),
        ParentId = parentId,
    };

    public void Rename(string name) => Name = name.Trim();
}
