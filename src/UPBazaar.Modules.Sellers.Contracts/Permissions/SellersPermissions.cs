namespace UPBazaar.Modules.Sellers.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too, and
/// because the policy provider turns any string here into a policy on demand - this list
/// is the whole registration.
///
/// Applying to sell needs no permission beyond being signed in: anyone may apply, and what a
/// seller may then do comes from the SellerOwner role granted on approval.
/// </summary>
public static class SellersPermissions
{
    /// <summary>See every seller and application.</summary>
    public const string Read = "sellers.read";

    /// <summary>Link an owner account to a seller that has none.</summary>
    public const string Write = "sellers.write";

    /// <summary>
    /// Approve or reject a seller's application. Separate from <see cref="Write"/> because
    /// approving is what lets a stranger sell to buyers and be paid for it.
    /// </summary>
    public const string KycApprove = "sellers.kyc.approve";

    /// <summary>
    /// A shop's owner running the shop itself: its team and where couriers collect from. Staff the
    /// owner adds can do the shop's work but not this.
    /// </summary>
    public const string OwnManage = "sellers.own.manage";

    /// <summary>Every permission this module defines, for seeding and policy generation.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Read,
        Write,
        KycApprove,
        OwnManage,
    ];
}
