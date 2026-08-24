namespace UPBazaar.Modules.Cart.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too, and
/// because the policy provider turns any string here into a policy on demand - this list
/// is the whole registration.
/// </summary>
public static class CartPermissions
{
    public const string Read = "cart.read";

    public const string Write = "cart.write";
}
