namespace UPBazaar.Modules.Payments.Gateway;

/// <summary>
/// Razorpay credentials, bound from <c>Payments:Razorpay</c>.
///
/// None of these belongs in appsettings.json. Locally they go in user-secrets; on a server, in
/// environment variables (<c>Payments__Razorpay__KeySecret</c>) or the host's secret store.
/// </summary>
public sealed class RazorpayOptions
{
    public const string SectionName = "Payments:Razorpay";

    /// <summary>Public key id, <c>rzp_test_...</c> or <c>rzp_live_...</c>. Sent to the browser.</summary>
    public string? KeyId { get; set; }

    /// <summary>Signs API calls and payment signatures. Never leaves the server.</summary>
    public string? KeySecret { get; set; }

    /// <summary>The secret set on the webhook in the Razorpay dashboard; signs each delivery.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Use the in-process fake gateway instead of Razorpay. Honoured only in Development and
    /// Testing; elsewhere the setting is ignored, so a stray flag cannot make production accept
    /// simulated payments.
    /// </summary>
    public bool UseFake { get; set; }

    /// <summary>True when every value the real gateway needs is present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(KeyId)
        && !string.IsNullOrWhiteSpace(KeySecret)
        && !string.IsNullOrWhiteSpace(WebhookSecret);
}
