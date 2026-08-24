using UPBazaar.Modules.Identity.Contracts.Permissions;

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
                PlatformJobsView,
            ]),

        new(
            RoleNames.CatalogModerator,
            "Reviews and corrects seller listings.",
            [IdentityPermissions.UsersRead]),

        new(
            RoleNames.FinanceOfficer,
            "Settlements, payouts and refunds.",
            [IdentityPermissions.UsersRead]),

        new(
            RoleNames.SupportAgent,
            "Answers customer and seller queries. Can look accounts up, not change them.",
            [IdentityPermissions.UsersRead]),

        new(
            RoleNames.SupportSupervisor,
            "Support lead. Can also act on the accounts an agent can only read.",
            [
                IdentityPermissions.UsersRead,
                IdentityPermissions.UsersManage,
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
            []),
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
