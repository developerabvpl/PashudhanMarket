namespace UPBazaar.Modules.Sellers.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too, and
/// because the policy provider turns any string here into a policy on demand - this list
/// is the whole registration.
/// </summary>
public static class SellersPermissions
{
    public const string Read = "sellers.read";

    public const string Write = "sellers.write";

    public const string KycApprove = "sellers.kyc.approve";
}
