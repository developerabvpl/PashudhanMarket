using System.ComponentModel.DataAnnotations;

namespace UPBazaar.Modules.Notifications;

/// <summary>
/// Where messages point people, bound from <c>Notifications</c>. The defaults are the local dev
/// servers; every deployed environment sets its own, or links in messages lead nowhere.
/// </summary>
public sealed class NotificationsModuleOptions
{
    public const string SectionName = "Notifications";

    /// <summary>The storefront's address, for links in buyers' texts.</summary>
    [Required]
    [Url]
    public string StorefrontUrl { get; set; } = "http://localhost:4200";

    /// <summary>The seller portal's address, for links in sellers' emails.</summary>
    [Required]
    [Url]
    public string SellerPortalUrl { get; set; } = "http://localhost:4201";
}
