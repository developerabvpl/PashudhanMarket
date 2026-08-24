using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Domain;

public enum ProductStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2,
}

public sealed class Product : AggregateRoot, IAuditable
{
    private Product()
    {
    }

    public Guid SellerId { get; private set; }

    public string Sku { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string Slug { get; private set; } = null!;

    public string? Description { get; private set; }

    public long CategoryId { get; private set; }

    public Category Category { get; private set; } = null!;

    public decimal Price { get; private set; }

    public string Currency { get; private set; } = "INR";

    public ProductStatus Status { get; private set; }

    public Stock Stock { get; private set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>Only an active product with a price can be bought.</summary>
    public bool IsPurchasable => Status == ProductStatus.Active && Price > 0;

    public static Product Create(
        Guid sellerId,
        string sku,
        string name,
        string slug,
        string? description,
        Category category,
        decimal price,
        string currency,
        int initialStock)
    {
        var product = new Product
        {
            SellerId = sellerId,
            Sku = sku.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Description = description?.Trim(),
            Category = category,
            Price = price,
            Currency = currency.Trim().ToUpperInvariant(),
            Status = ProductStatus.Draft,
            Stock = Stock.Create(initialStock),
        };

        product.Raise(new ProductCreatedDomainEvent(product.PublicId, product.Sku, product.SellerId));

        return product;
    }

    public Result Publish()
    {
        if (Price <= 0)
        {
            return Result.Failure(CatalogErrors.ProductNotPurchasable);
        }

        Status = ProductStatus.Active;
        Raise(new ProductPublishedDomainEvent(PublicId, Sku, Price, Currency));

        return Result.Success();
    }

    public void Archive() => Status = ProductStatus.Archived;

    public Result ChangePrice(decimal price)
    {
        if (price <= 0)
        {
            return Result.Failure(
                Error.Validation("catalog.product.invalid_price", "Price must be greater than zero."));
        }

        Price = price;
        return Result.Success();
    }
}
