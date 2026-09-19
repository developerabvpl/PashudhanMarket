namespace UPBazaar.Modules.Catalog.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too, and
/// because the policy provider turns any string here into a policy on demand - this list
/// is the whole registration.
///
/// Browsing the published catalogue needs no permission at all. These cover what a shopper
/// never sees: drafts, archived listings and every change.
/// </summary>
public static class CatalogPermissions
{
    /// <summary>See listings in every status, not only the published ones.</summary>
    public const string ProductsRead = "catalog.products.read";

    /// <summary>Create, edit, publish and archive listings, and adjust their stock.</summary>
    public const string ProductsWrite = "catalog.products.write";

    /// <summary>Create and rename categories.</summary>
    public const string CategoriesWrite = "catalog.categories.write";

    /// <summary>Every permission this module defines, for seeding and policy generation.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        ProductsRead,
        ProductsWrite,
        CategoriesWrite,
    ];
}
