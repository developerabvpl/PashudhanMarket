namespace UPBazaar.Modules.Catalog.Contracts;

/// <summary>What other modules may ask the catalogue.</summary>
public interface IProductCatalog
{
    /// <summary>
    /// Whether a product exists in any status. Inventory asks before recording stock, so a
    /// mistyped id cannot leave stock that belongs to nothing.
    /// </summary>
    Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken);
}
