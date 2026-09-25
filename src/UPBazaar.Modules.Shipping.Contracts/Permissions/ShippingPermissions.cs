namespace UPBazaar.Modules.Shipping.Contracts.Permissions;

/// <summary>
/// Permission names used in <c>[Authorize(...)]</c> on this module's endpoints.
///
/// They live in Contracts because other modules and the front ends name them too, and
/// because the policy provider turns any string here into a policy on demand - this list
/// is the whole registration. A buyer tracks their own parcels with <c>orders.own.read</c>;
/// nothing here is needed for that.
/// </summary>
public static class ShippingPermissions
{
    /// <summary>See every shipment and the pickup locations.</summary>
    public const string ShipmentsRead = "shipping.shipments.read";

    /// <summary>Book couriers for packed parcels and manage pickup locations.</summary>
    public const string ShipmentsWrite = "shipping.shipments.write";

    /// <summary>A seller packing and booking couriers for their own parts, and setting their pickup location.</summary>
    public const string OwnShipmentsWrite = "shipping.shipments.own.write";

    /// <summary>See what cash-on-delivery money the courier owes, and what it has paid over.</summary>
    public const string CodRead = "shipping.cod.read";

    /// <summary>Upload the courier's remittance reports and write off what will never come.</summary>
    public const string CodWrite = "shipping.cod.write";

    /// <summary>Every permission this module defines, for seeding and policy generation.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        ShipmentsRead,
        ShipmentsWrite,
        OwnShipmentsWrite,
        CodRead,
        CodWrite,
    ];
}
