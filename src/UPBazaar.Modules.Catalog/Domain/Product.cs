using UPBazaar.Modules.Catalog.Contracts.Events;
using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Domain;

/// <summary>Where a listing is in its life.</summary>
public enum ProductStatus
{
    /// <summary>Being prepared. Invisible to shoppers.</summary>
    Draft = 0,

    /// <summary>Published and purchasable.</summary>
    Active = 1,

    /// <summary>Withdrawn for good. Kept because orders reference it.</summary>
    Archived = 2,
}

/// <summary>
/// One listing: what it is, what it costs and who sells it. How many exist is Inventory's
/// business; the API fills stock into product responses from there.
/// </summary>
public sealed class Product : AggregateRoot, IAuditable
{
    /// <summary>The only currency the platform trades in.</summary>
    public const string DefaultCurrency = "INR";

    private Product()
    {
    }

    /// <summary>Seller-facing stock-keeping unit, unique across the catalogue.</summary>
    public string Sku { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>URL segment for readable links. Not unique: two sellers may list the same thing.</summary>
    public string Slug { get; private set; } = null!;

    public string? Brand { get; private set; }

    public string? Description { get; private set; }

    public decimal Price { get; private set; }

    public string Currency { get; private set; } = DefaultCurrency;

    public ProductStatus Status { get; private set; }

    /// <summary>
    /// Public id of the selling account. A plain id rather than a key: sellers belong to the
    /// Sellers module, and a module never holds a foreign key into another module's schema.
    /// </summary>
    public Guid SellerId { get; private set; }

    public long CategoryId { get; private set; }

    public Category Category { get; private set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>Creates a draft. Nothing is visible to shoppers until <see cref="Publish"/>.</summary>
    /// <param name="sku">Unique stock-keeping unit, already normalised.</param>
    /// <param name="name">Listing title.</param>
    /// <param name="brand">Brand, if any.</param>
    /// <param name="description">Long description, if any.</param>
    /// <param name="price">Price in <see cref="DefaultCurrency"/>.</param>
    /// <param name="sellerId">Public id of the selling account.</param>
    /// <param name="category">Category to list under.</param>
    /// <param name="publicId">
    /// Fixed public id, used only when importing a catalogue whose ids are already published.
    /// </param>
    public static Product CreateDraft(
        string sku,
        string name,
        string? brand,
        string? description,
        decimal price,
        Guid sellerId,
        Category category,
        Guid? publicId = null)
    {
        ArgumentNullException.ThrowIfNull(category);

        var product = new Product
        {
            Sku = sku,
            Name = name,
            Slug = Domain.Slug.From(name, sku),
            Brand = brand,
            Description = description,
            Price = price,
            SellerId = sellerId,
            Category = category,
            Status = ProductStatus.Draft,
        };

        if (publicId is { } id)
        {
            product.PublicId = id;
        }

        return product;
    }

    /// <summary>Changes what the listing says and costs. Archived listings are frozen.</summary>
    public Result UpdateDetails(
        string name,
        string? brand,
        string? description,
        decimal price,
        Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (Status == ProductStatus.Archived)
        {
            return Result.Failure(CatalogErrors.ProductArchived);
        }

        // Only a published price is one somebody may have put in a cart; a draft can be
        // re-priced as often as its seller likes without anyone needing to hear about it.
        if (price != Price && Status == ProductStatus.Active)
        {
            Raise(new ProductPriceChangedDomainEvent(PublicId, Price, price, Currency));
        }

        Name = name;
        Slug = Domain.Slug.From(name, Sku);
        Brand = brand;
        Description = description;
        Price = price;
        Category = category;
        CategoryId = category.Id;

        return Result.Success();
    }

    /// <summary>Makes the listing visible. Publishing an active listing is a no-op.</summary>
    public Result Publish()
    {
        switch (Status)
        {
            case ProductStatus.Archived:
                return Result.Failure(CatalogErrors.ProductArchived);
            case ProductStatus.Active:
                return Result.Success();
        }

        Status = ProductStatus.Active;
        Raise(new ProductPublishedDomainEvent(PublicId, SellerId, Sku));

        return Result.Success();
    }

    /// <summary>
    /// Withdraws the listing for good. There is no un-archive: a returning product is a new
    /// listing, so order history never points at something that changed underneath it.
    /// </summary>
    public void Archive()
    {
        if (Status == ProductStatus.Archived)
        {
            return;
        }

        Status = ProductStatus.Archived;
        Raise(new ProductArchivedDomainEvent(PublicId, SellerId, Sku));
    }
}
