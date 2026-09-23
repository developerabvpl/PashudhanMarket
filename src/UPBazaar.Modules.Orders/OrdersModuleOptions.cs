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

    public TimeSpan ReturnWindow => TimeSpan.FromDays(ReturnWindowDays);
}
