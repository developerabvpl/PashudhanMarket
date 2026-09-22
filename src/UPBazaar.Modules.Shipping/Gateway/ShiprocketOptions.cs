namespace UPBazaar.Modules.Shipping.Gateway;

/// <summary>
/// Shiprocket credentials, bound from <c>Shipping:Shiprocket</c>.
///
/// Shiprocket's API signs in with an API user's email and password - a user created for the
/// purpose under Settings, API, not the account owner's login. None of these belongs in
/// appsettings.json: user-secrets locally, environment variables or a secret store on a server.
/// </summary>
public sealed class ShiprocketOptions
{
    public const string SectionName = "Shipping:Shiprocket";

    /// <summary>The API user's email.</summary>
    public string? Email { get; set; }

    /// <summary>The API user's password.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// The token set on the tracking webhook in the Shiprocket dashboard; Shiprocket sends it
    /// back in the <c>x-api-key</c> header of every update.
    /// </summary>
    public string? WebhookToken { get; set; }

    /// <summary>Use the in-process fake. Honoured only in Development and Testing.</summary>
    public bool UseFake { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(Password)
        && !string.IsNullOrWhiteSpace(WebhookToken);
}
