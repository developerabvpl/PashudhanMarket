namespace UPBazaar.Modules.Inventory.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too, and
/// because the policy provider turns any string here into a policy on demand - this list
/// is the whole registration.
/// </summary>
public static class InventoryPermissions
{
    /// <summary>See stock levels, the movement ledger and reservations.</summary>
    public const string StockRead = "inventory.stock.read";

    /// <summary>Record deliveries and stock-takes.</summary>
    public const string StockWrite = "inventory.stock.write";

    /// <summary>
    /// Write stock off: damage, loss, expiry. Separate from <see cref="StockWrite"/> because a
    /// write-off is the one movement that can hide theft, so it is granted more narrowly.
    /// </summary>
    public const string AdjustmentsApprove = "inventory.adjustments.approve";

    /// <summary>A seller recording how many of their own products they have.</summary>
    public const string OwnStockWrite = "inventory.stock.own.write";

    /// <summary>Every permission this module defines, for seeding and policy generation.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        StockRead,
        StockWrite,
        AdjustmentsApprove,
        OwnStockWrite,
    ];
}
