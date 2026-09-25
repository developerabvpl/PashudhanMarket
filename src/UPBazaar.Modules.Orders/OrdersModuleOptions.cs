using System.ComponentModel.DataAnnotations;

namespace UPBazaar.Modules.Orders;

/// <summary>Orders policy that can change without a release, bound from <c>Orders</c>.</summary>
public sealed class OrdersModuleOptions
{
    public const string SectionName = "Orders";

    /// <summary>
    /// Days after delivery a buyer may ask to return a parcel. Each parcel's deadline is fixed
    /// when it is delivered, so changing this affects only parcels delivered afterwards.
    /// </summary>
    [Range(1, 60)]
    public int ReturnWindowDays { get; set; } = 7;

    /// <summary>
    /// The delivery charge on an order, in rupees, however many sellers' parcels it makes. Set it
    /// to 0 for free delivery on everything. Changing it affects only orders placed afterwards:
    /// each order keeps the charge it was placed with.
    /// </summary>
    [Range(0, 10_000)]
    public decimal DeliveryFee { get; set; } = 49m;

    /// <summary>
    /// Orders whose goods come to at least this much, in rupees, are delivered free. 0 charges
    /// every order.
    /// </summary>
    [Range(0, 1_000_000)]
    public decimal FreeDeliveryFrom { get; set; } = 499m;

    public TimeSpan ReturnWindow => TimeSpan.FromDays(ReturnWindowDays);

    /// <summary>The delivery charge for an order whose goods come to <paramref name="subtotal"/>.</summary>
    public decimal DeliveryFeeFor(decimal subtotal) =>
        FreeDeliveryFrom > 0 && subtotal >= FreeDeliveryFrom ? 0m : DeliveryFee;
}
