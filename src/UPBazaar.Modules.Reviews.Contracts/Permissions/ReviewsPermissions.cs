namespace UPBazaar.Modules.Reviews.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too. A name only
/// becomes a grantable permission once <c>PermissionCatalog</c> in Identity lists <see cref="All"/>.
/// </summary>
public static class ReviewsPermissions
{
    /// <summary>See every review, including text still waiting for approval.</summary>
    public const string Read = "reviews.read";

    /// <summary>Approve or reject what buyers wrote, and hide sellers' replies.</summary>
    public const string Moderate = "reviews.moderate";

    /// <summary>A buyer rates and reviews what was delivered to them.</summary>
    public const string OwnWrite = "reviews.own.write";

    /// <summary>A seller reads the reviews of their products and replies to them.</summary>
    public const string SellerReply = "reviews.seller.reply";

    /// <summary>Every permission this module defines, for seeding and policy generation.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Read,
        Moderate,
        OwnWrite,
        SellerReply,
    ];
}
