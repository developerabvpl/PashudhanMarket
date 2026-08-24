using System.ComponentModel.DataAnnotations;

namespace UPBazaar.Modules.Identity.Services;

/// <summary>
/// Everything about the identity module that an operator may need to tune, with defaults that
/// match the security posture the module was designed around.
/// </summary>
public sealed class IdentityModuleOptions
{
    public const string SectionName = "Identity";

    /// <summary>Access token lifetime. Short, because a refresh token can always mint another.</summary>
    [Range(1, 120)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh token lifetime, and therefore how long "stay signed in" lasts.</summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>How long a one-time code stays valid.</summary>
    [Range(1, 30)]
    public int OtpLifetimeMinutes { get; set; } = 5;

    /// <summary>Codes one mobile number may request inside the rate-limit window.</summary>
    [Range(1, 20)]
    public int OtpPerMobilePerWindow { get; set; } = 3;

    /// <summary>
    /// Codes one IP address may request inside the window. Higher than the per-mobile limit
    /// because a family or an office shares an address, but low enough to stop a script
    /// walking through numbers.
    /// </summary>
    [Range(1, 200)]
    public int OtpPerIpPerWindow { get; set; } = 15;

    /// <summary>Rate-limit window for OTP requests.</summary>
    [Range(1, 120)]
    public int OtpRateLimitWindowMinutes { get; set; } = 15;

    /// <summary>Issuer name shown in authenticator apps.</summary>
    public string TotpIssuer { get; set; } = "UP Bazaar";

    /// <summary>Where a password-reset link points; the token is appended as a query string.</summary>
    public string PasswordResetUrl { get; set; } = "https://upbazaar.example/reset-password";

    /// <summary>Seed account created on first run when both values are present.</summary>
    public SuperAdminSeedOptions SuperAdmin { get; set; } = new();
}

/// <summary>
/// The first administrator.
///
/// Read from configuration - in practice the UPBAZAAR_Identity__SuperAdmin__* environment
/// variables - and used only when no account with that email exists. Leaving it unset simply
/// skips seeding, which is the right behaviour for an environment that already has one.
/// </summary>
public sealed class SuperAdminSeedOptions
{
    [EmailAddress]
    public string? Email { get; set; }

    public string? Password { get; set; }

    public string DisplayName { get; set; } = "Super Admin";

    /// <summary>True when both an email and a password were supplied.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}
