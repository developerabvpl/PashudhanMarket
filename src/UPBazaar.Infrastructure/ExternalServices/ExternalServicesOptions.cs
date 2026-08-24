using System.ComponentModel.DataAnnotations;

namespace UPBazaar.Infrastructure.ExternalServices;

/// <summary>
/// Toggle between the in-process fakes and live adapters. Sandbox is the default so a fresh
/// clone runs, and tests never reach the network.
/// </summary>
public sealed class ExternalServicesOptions
{
    public const string SectionName = "ExternalServices";

    /// <summary>When true, every external dependency is served by its Fake implementation.</summary>
    public bool UseSandbox { get; set; } = true;

    public RazorpayOptions Razorpay { get; set; } = new();

    public ShiprocketOptions Shiprocket { get; set; } = new();
}

public sealed class RazorpayOptions
{
    public string BaseUrl { get; set; } = "https://api.razorpay.com/v1/";

    /// <summary>Read from user-secrets or environment, never from appsettings.</summary>
    public string KeyId { get; set; } = string.Empty;

    public string KeySecret { get; set; } = string.Empty;

    /// <summary>Secret used to verify inbound webhook signatures.</summary>
    public string WebhookSecret { get; set; } = string.Empty;
}

public sealed class ShiprocketOptions
{
    public string BaseUrl { get; set; } = "https://apiv2.shiprocket.in/v1/external/";

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    [Range(1, 100)]
    public int DefaultPickupPostcodeLength { get; set; } = 6;
}
