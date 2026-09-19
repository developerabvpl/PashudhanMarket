namespace UPBazaar.Modules.Cart.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too, and
/// because the policy provider turns any string here into a policy on demand - this list
/// is the whole registration.
///
/// Both are granted to the Buyer role. A cart is always the caller's own: nothing here lets one
/// account see or change another's.
/// </summary>
public static class CartPermissions
{
    /// <summary>See your own cart.</summary>
    public const string Read = "cart.read";

    /// <summary>Change your own cart.</summary>
    public const string Write = "cart.write";

    /// <summary>Every permission this module defines, for seeding and policy generation.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Read,
        Write,
    ];
}
