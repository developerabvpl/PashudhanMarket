namespace UPBazaar.Modules.Orders.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too, and
/// because the policy provider turns any string here into a policy on demand - this list
/// is the whole registration.
///
/// Two families: the <c>orders.own.*</c> pair lets a buyer reach their own orders and nobody
/// else's, the way <c>cart.*</c> does; the rest are staff permissions over every order.
/// </summary>
public static class OrdersPermissions
{
    /// <summary>See your own orders.</summary>
    public const string OwnRead = "orders.own.read";

    /// <summary>Place orders, and cancel your own before they ship.</summary>
    public const string OwnWrite = "orders.own.write";

    /// <summary>See every order.</summary>
    public const string Read = "orders.read";

    /// <summary>Move a seller's part of an order along: packed, shipped, delivered.</summary>
    public const string Write = "orders.write";

    /// <summary>
    /// Cancel any order or part of one. Separate from <see cref="Write"/> because a cancellation
    /// can mean a refund, so it is granted more narrowly than day-to-day fulfilment.
    /// </summary>
    public const string Cancel = "orders.cancel";

    /// <summary>Every permission this module defines, for seeding and policy generation.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        OwnRead,
        OwnWrite,
        Read,
        Write,
        Cancel,
    ];
}
