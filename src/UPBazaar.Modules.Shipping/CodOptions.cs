using System.ComponentModel.DataAnnotations;

namespace UPBazaar.Modules.Shipping;

/// <summary>Cash on delivery: how long the courier may take to pay over what it collected.</summary>
public sealed class CodOptions
{
    public const string SectionName = "Shipping:Cod";

    /// <summary>
    /// Days after delivery that a parcel's cash is overdue if the courier has not paid it over.
    /// Shiprocket's cycle is about a week.
    /// </summary>
    [Range(1, 90)]
    public int OverdueDays { get; set; } = 7;

    public TimeSpan OverdueAfter => TimeSpan.FromDays(OverdueDays);
}
