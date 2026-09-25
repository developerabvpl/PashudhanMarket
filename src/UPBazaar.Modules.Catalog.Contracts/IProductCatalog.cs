namespace UPBazaar.Modules.Catalog.Contracts;

/// <summary>What other modules may ask the catalogue.</summary>
public interface IProductCatalog
{
    /// <summary>
    /// Whether a product exists in any status. Inventory asks before recording stock, so a
    /// mistyped id cannot leave stock that belongs to nothing.
    /// </summary>
    Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// The listing facts a buyer-facing module needs, for each id that exists in any status.
    /// Ids that name nothing are left out, so a caller can tell "gone" from "not for sale".
    /// </summary>
    Task<IReadOnlyDictionary<Guid, CatalogProductDto>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// How each product ships, for Shipping to size a parcel. Products whose package has not been
    /// recorded are left out, so the caller can say exactly which ones need measuring.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Dtos.ProductPackageDto>> GetPackagesAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);
}

/// <summary>A product as another module sees it: what it is called, what it costs, whether it can be bought.</summary>
/// <param name="Id">Public id.</param>
/// <param name="Sku">Stock-keeping unit.</param>
/// <param name="Name">Listing title.</param>
/// <param name="Price">What it costs now: the sale price while a sale runs, else the regular price.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="SellerId">Public id of the selling account.</param>
/// <param name="IsPurchasable">Published and priced: something a shopper may buy right now.</param>
public sealed record CatalogProductDto(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    Guid SellerId,
    bool IsPurchasable);
