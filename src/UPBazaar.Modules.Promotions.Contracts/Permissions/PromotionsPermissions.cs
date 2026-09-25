namespace UPBazaar.Modules.Promotions.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too. A name only
/// becomes a grantable permission once <c>PermissionCatalog</c> in Identity lists <see cref="All"/>.
/// </summary>
public static class PromotionsPermissions
{
    /// <summary>See every coupon, and which orders used them.</summary>
    public const string CampaignsRead = "promotions.campaigns.read";

    /// <summary>Create and end the platform's coupons: money off that the platform, or opted-in sellers, pay for.</summary>
    public const string CampaignsWrite = "promotions.campaigns.write";

    /// <summary>A seller runs coupons on their own products, and joins platform campaigns they pay for.</summary>
    public const string OwnWrite = "promotions.own.write";

    /// <summary>Every permission this module defines, for seeding and policy generation.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        CampaignsRead,
        CampaignsWrite,
        OwnWrite,
    ];
}
