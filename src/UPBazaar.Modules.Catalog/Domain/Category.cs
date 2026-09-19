using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Catalog.Domain;

/// <summary>
/// A shelf in the catalogue. Categories nest through <see cref="ParentId"/>; the storefront's
/// category rail is the top level.
/// </summary>
public sealed class Category : Entity, IAuditable
{
    private Category()
    {
    }

    public string Name { get; private set; } = null!;

    /// <summary>Unique URL segment, derived from the name.</summary>
    public string Slug { get; private set; } = null!;

    /// <summary>Internal key of the parent category, or null at the top level.</summary>
    public long? ParentId { get; private set; }

    public Category? Parent { get; private set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <param name="name">Display name.</param>
    /// <param name="parent">Parent category, or null for the top level.</param>
    /// <param name="publicId">
    /// Fixed public id, used only when importing a catalogue whose ids are already published.
    /// Null for anything created through the API.
    /// </param>
    public static Category Create(string name, Category? parent, Guid? publicId = null)
    {
        var category = new Category
        {
            Name = name,
            Slug = Domain.Slug.From(name, "category"),
            Parent = parent,
        };

        if (publicId is { } id)
        {
            category.PublicId = id;
        }

        return category;
    }

    public void Rename(string name)
    {
        Name = name;
        Slug = Domain.Slug.From(name, "category");
    }

    public void MoveUnder(Category? parent)
    {
        Parent = parent;
        ParentId = parent?.Id;
    }
}
