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

    /// <summary>A seller's draft waiting for a moderator to publish it or send it back. Invisible to shoppers.</summary>
    InReview = 3,
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

    /// <summary>The regular price: what the listing costs when no sale is running.</summary>
    public decimal Price { get; private set; }

    /// <summary>
    /// What it costs while its sale runs; null with no sale set. The seller bears the difference -
    /// the sale price simply is the price while it lasts, and is what they are paid on.
    /// </summary>
    public decimal? SalePrice { get; private set; }

    /// <summary>When the sale price takes over.</summary>
    public DateTime? SaleStartsAtUtc { get; private set; }

    /// <summary>When the regular price comes back. Every sale ends: a price that never does is just the price.</summary>
    public DateTime? SaleEndsAtUtc { get; private set; }

    public string Currency { get; private set; } = DefaultCurrency;

    public ProductStatus Status { get; private set; }

    /// <summary>
    /// Public id of the selling account. A plain id rather than a key: sellers belong to the
    /// Sellers module, and a module never holds a foreign key into another module's schema.
    /// </summary>
    public Guid SellerId { get; private set; }

    public long CategoryId { get; private set; }

    public Category Category { get; private set; } = null!;

    /// <summary>
    /// Packed weight in grams, for couriers. The four package values are set together or not at
    /// all: a courier quote needs every one of them, so a half-filled parcel is no parcel.
    /// </summary>
    public int? WeightGrams { get; private set; }

    /// <summary>Packed length in centimetres.</summary>
    public decimal? LengthCm { get; private set; }

    /// <summary>Packed breadth in centimetres.</summary>
    public decimal? BreadthCm { get; private set; }

    /// <summary>Packed height in centimetres.</summary>
    public decimal? HeightCm { get; private set; }

    /// <summary>Why a moderator sent the listing back, while it is back in Draft; cleared on publishing.</summary>
    public string? ReviewNote { get; private set; }

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
        Category category,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (Status == ProductStatus.Archived)
        {
            return Result.Failure(CatalogErrors.ProductArchived);
        }

        if (!StaysAboveSale(price, now))
        {
            return Result.Failure(CatalogErrors.PriceNotAboveSale);
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

    /// <summary>
    /// Records how one unit ships: weight and box size, as packed. Null clears it. Allowed on an
    /// archived listing too, since orders placed before archiving may still need to ship.
    /// </summary>
    public void SetPackage(int? weightGrams, decimal? lengthCm, decimal? breadthCm, decimal? heightCm)
    {
        var all = weightGrams is not null && lengthCm is not null && breadthCm is not null && heightCm is not null;

        WeightGrams = all ? weightGrams : null;
        LengthCm = all ? lengthCm : null;
        BreadthCm = all ? breadthCm : null;
        HeightCm = all ? heightCm : null;
    }

    /// <summary>A seller asks for their draft to be published. Only a draft can be submitted.</summary>
    public Result SubmitForReview()
    {
        if (Status != ProductStatus.Draft)
        {
            return Result.Failure(CatalogErrors.NotADraft);
        }

        Status = ProductStatus.InReview;

        return Result.Success();
    }

    /// <summary>A moderator sends a listing back to its seller as a draft, saying why.</summary>
    public Result SendBack(string note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        if (Status != ProductStatus.InReview)
        {
            return Result.Failure(CatalogErrors.NotInReview);
        }

        Status = ProductStatus.Draft;
        ReviewNote = note.Trim();

        Raise(new ProductSentBackDomainEvent(PublicId, SellerId, Name, ReviewNote));

        return Result.Success();
    }

    /// <summary>
    /// Changes only the price - what a seller may still do to a live listing without a new review,
    /// since a price is operational rather than a claim about the product. Buyers with it in their
    /// cart see the change flagged, exactly as when staff re-price it.
    /// </summary>
    public Result Reprice(decimal price, DateTime now)
    {
        if (Status == ProductStatus.Archived)
        {
            return Result.Failure(CatalogErrors.ProductArchived);
        }

        if (!StaysAboveSale(price, now))
        {
            return Result.Failure(CatalogErrors.PriceNotAboveSale);
        }

        if (price != Price && Status == ProductStatus.Active)
        {
            Raise(new ProductPriceChangedDomainEvent(PublicId, Price, price, Currency));
        }

        Price = price;

        return Result.Success();
    }

    /// <summary>Whether its sale is running at <paramref name="now"/>.</summary>
    public bool IsSaleRunning(DateTime now) => SaleRuns(SalePrice, SaleStartsAtUtc, SaleEndsAtUtc, now);

    /// <summary>What it costs at <paramref name="now"/>: the sale price while the sale runs, else the regular price.</summary>
    public decimal PriceAt(DateTime now) => IsSaleRunning(now) ? SalePrice!.Value : Price;

    /// <summary>
    /// <see cref="PriceAt(DateTime)"/> from bare columns, for queries that project a product rather than
    /// load it.
    /// </summary>
    public static decimal PriceAt(decimal price, decimal? salePrice, DateTime? startsAtUtc, DateTime? endsAtUtc, DateTime now) =>
        SaleRuns(salePrice, startsAtUtc, endsAtUtc, now) ? salePrice!.Value : price;

    /// <summary>
    /// Puts the listing on sale between two moments, replacing any sale already set. The sale
    /// price must be below the regular price - otherwise it is not a sale - and the sale must
    /// still have time to run.
    /// </summary>
    public Result SetSale(decimal salePrice, DateTime startsAtUtc, DateTime endsAtUtc, DateTime now)
    {
        if (Status == ProductStatus.Archived)
        {
            return Result.Failure(CatalogErrors.ProductArchived);
        }

        if (salePrice <= 0 || salePrice >= Price)
        {
            return Result.Failure(CatalogErrors.SaleNotBelowPrice);
        }

        if (endsAtUtc <= startsAtUtc || endsAtUtc <= now)
        {
            return Result.Failure(CatalogErrors.SaleEndsTooSoon);
        }

        SalePrice = salePrice;
        SaleStartsAtUtc = startsAtUtc;
        SaleEndsAtUtc = endsAtUtc;

        return Result.Success();
    }

    /// <summary>Ends the sale now, or calls off one still to come. Nothing to end is not an error.</summary>
    public void EndSale()
    {
        SalePrice = null;
        SaleStartsAtUtc = null;
        SaleEndsAtUtc = null;
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
        ReviewNote = null;
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

    private static bool SaleRuns(decimal? salePrice, DateTime? startsAtUtc, DateTime? endsAtUtc, DateTime now) =>
        salePrice is not null && now >= startsAtUtc && now < endsAtUtc;

    /// <summary>
    /// A regular price must stay above a sale that has not finished, or the "sale" would cost
    /// more than the listing. A finished sale no longer matters, so it is cleared here rather
    /// than left to stand in the way.
    /// </summary>
    private bool StaysAboveSale(decimal price, DateTime now)
    {
        if (SaleEndsAtUtc <= now)
        {
            EndSale();
        }

        return SalePrice is not { } sale || price > sale;
    }
}
