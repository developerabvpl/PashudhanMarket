namespace UPBazaar.Modules.Identity.Contracts.Dtos;

/// <summary>A freshly issued token pair.</summary>
/// <param name="AccessToken">JWT to send as a bearer token.</param>
/// <param name="ExpiresInSeconds">Lifetime of the access token.</param>
/// <param name="RefreshToken">Opaque token used once to obtain the next pair.</param>
/// <param name="RefreshTokenExpiresAtUtc">When the refresh token stops working.</param>
public sealed record AuthTokensDto(
    string AccessToken,
    int ExpiresInSeconds,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);

/// <summary>
/// Outcome of an authentication attempt.
///
/// A staff member with 2FA enabled gets <see cref="RequiresTwoFactor"/> and no tokens: the
/// password was right, but the second factor has not been presented yet.
/// </summary>
/// <param name="Tokens">Tokens, when authentication completed.</param>
/// <param name="RequiresTwoFactor">True when a TOTP code is still needed.</param>
/// <param name="TwoFactorToken">Short-lived token identifying the half-finished sign-in.</param>
public sealed record AuthResultDto(
    AuthTokensDto? Tokens,
    bool RequiresTwoFactor = false,
    string? TwoFactorToken = null);

/// <summary>What the caller needs to enrol an authenticator app.</summary>
/// <param name="SharedKey">Base32 secret, for manual entry.</param>
/// <param name="AuthenticatorUri">otpauth:// URI, for a QR code.</param>
public sealed record TotpSetupDto(string SharedKey, string AuthenticatorUri);
