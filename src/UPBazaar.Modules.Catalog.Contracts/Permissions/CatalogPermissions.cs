namespace UPBazaar.Modules.Catalog.Contracts.Permissions;

/// <summary>
/// Permission names used in [Authorize(...)] on catalog endpoints. The policy provider turns
/// each one into a policy on demand, so this list is the whole registration.
/// </summary>
public static class CatalogPermissions
{
    public const string ProductsRead = "catalog.products.read";

    public const string ProductsWrite = "catalog.products.write";

    public const string StockWrite = "catalog.stock.write";

    public const string CategoriesWrite = "catalog.categories.write";
}
