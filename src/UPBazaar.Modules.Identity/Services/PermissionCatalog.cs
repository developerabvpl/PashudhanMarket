using UPBazaar.Modules.Cart.Contracts.Permissions;
using UPBazaar.Modules.Catalog.Contracts.Permissions;
using UPBazaar.Modules.Identity.Contracts.Permissions;
using UPBazaar.Modules.Inventory.Contracts.Permissions;
using UPBazaar.Modules.Orders.Contracts.Permissions;
using UPBazaar.Modules.Payments.Contracts.Permissions;
using UPBazaar.Modules.Shipping.Contracts.Permissions;

namespace UPBazaar.Modules.Identity.Services;

/// <summary>
/// Every permission the platform recognises, and the roles that bundle them.
///
/// This is the single source of truth: authorization policies are generated from it at
/// startup, the <c>identity.Permissions</c> table is seeded from it, and a permission that is
/// not listed here cannot be granted. Modules still declare their own names in their Contracts
/// projects; this catalogue is where those lists are brought together.
///
/// Only the modules that exist as more than a skeleton contribute names today. As each module
/// grows endpoints, its permission class is added here in one line.
/// </summary>
public static class PermissionCatalog
{
    /// <summary>Permission granted to whoever may open the Hangfire dashboard.</summary>
    public const string PlatformJobsView = "platform.jobs.view";

    /// <summary>Every known permission name.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        .. IdentityPermissions.All,
        .. CatalogPermissions.All,
        .. InventoryPermissions.All,
        .. CartPermissions.All,
        .. OrdersPermissions.All,
        .. PaymentsPermissions.All,
        .. ShippingPermissions.All,
        PlatformJobsView,
    ];

    /// <summary>
    /// Seed roles and what each one grants.
    ///
    /// SuperAdmin is intentionally absent: it is granted everything in <see cref="All"/> at
    /// seed time, so a new permission is covered the moment it is added rather than the next
    /// time somebody remembers to update a list.
    /// </summary>
    public static IReadOnlyList<RoleDefinition> Roles { get; } =
    [
        new(
            RoleNames.SuperAdmin,
            "Unrestricted access. Holds every permission, including ones added later.",
            All),

        new(
            RoleNames.Admin,
            "Day-to-day platform administration, short of unrestricted access.",
            [
                IdentityPermissions.UsersRead,
                IdentityPermissions.UsersManage,
                IdentityPermissions.RolesRead,
                CatalogPermissions.ProductsRead,
                CatalogPermissions.ProductsWrite,
                CatalogPermissions.CategoriesWrite,
                InventoryPermissions.StockRead,
                InventoryPermissions.StockWrite,
                InventoryPermissions.AdjustmentsApprove,
                OrdersPermissions.Read,
                OrdersPermissions.Write,
                OrdersPermissions.Cancel,
                PaymentsPermissions.Read,
                PaymentsPermissions.RefundsWrite,
                ShippingPermissions.ShipmentsRead,
                ShippingPermissions.ShipmentsWrite,
                PlatformJobsView,
            ]),

        new(
            RoleNames.CatalogModerator,
            "Reviews and corrects seller listings.",
            [
                IdentityPermissions.UsersRead,
                CatalogPermissions.ProductsRead,
                CatalogPermissions.ProductsWrite,
                CatalogPermissions.CategoriesWrite,

                // Moderators correct listings, including their stock figures, but writing stock
                // off stays with Admin: that is a loss to account for, not a listing fix.
                InventoryPermissions.StockRead,
                InventoryPermissions.StockWrite,
            ]),

        new(
            RoleNames.FinanceOfficer,
            "Settlements, payouts and refunds.",
            [
                IdentityPermissions.UsersRead,
                OrdersPermissions.Read,
                PaymentsPermissions.Read,
                PaymentsPermissions.RefundsWrite,
            ]),

        new(
            RoleNames.SupportAgent,
            "Answers customer and seller queries. Can look accounts up, not change them.",
            [
                IdentityPermissions.UsersRead,
                OrdersPermissions.Read,
                PaymentsPermissions.Read,
                ShippingPermissions.ShipmentsRead,
            ]),

        new(
            RoleNames.SupportSupervisor,
            "Support lead. Can also act on the accounts an agent can only read.",
            [
                IdentityPermissions.UsersRead,
                IdentityPermissions.UsersManage,
                OrdersPermissions.Read,

                // A supervisor can cancel for a buyer who cannot, but does not run fulfilment.
                OrdersPermissions.Cancel,
                PaymentsPermissions.Read,
                ShippingPermissions.ShipmentsRead,
            ]),

        new(
            RoleNames.AcademyAuthor,
            "Writes and publishes seller training material.",
            []),

        new(
            RoleNames.SellerOwner,
            "Owns a seller account and everything under it.",
            []),

        new(
            RoleNames.Buyer,
            "Default role for a shopper. Carries no administrative permission.",
            [
                CartPermissions.Read,
                CartPermissions.Write,
                OrdersPermissions.OwnRead,
                OrdersPermissions.OwnWrite,
                PaymentsPermissions.OwnWrite,
            ]),
    ];

    /// <summary>Roles that exist in every environment.</summary>
    public static class RoleNames
    {
        public const string SuperAdmin = "SuperAdmin";
        public const string Admin = "Admin";
        public const string CatalogModerator = "CatalogModerator";
        public const string FinanceOfficer = "FinanceOfficer";
        public const string SupportAgent = "SupportAgent";
        public const string SupportSupervisor = "SupportSupervisor";
        public const string AcademyAuthor = "AcademyAuthor";
        public const string SellerOwner = "SellerOwner";
        public const string Buyer = "Buyer";
    }
}

/// <summary>One seed role.</summary>
/// <param name="Name">Role name, unique.</param>
/// <param name="Description">What the role is for, shown in the admin UI.</param>
/// <param name="Permissions">Permission names the role grants.</param>
public sealed record RoleDefinition(
    string Name,
    string Description,
    IReadOnlyList<string> Permissions);
