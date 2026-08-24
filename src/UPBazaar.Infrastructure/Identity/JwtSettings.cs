using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace UPBazaar.Infrastructure.Identity;

/// <summary>
/// The bearer token parameters, resolved once and shared.
///
/// Registered as a singleton so the code that validates tokens and the code that issues them
/// cannot disagree. They did once: the host generated an ephemeral Development key for
/// validation while the issuer read configuration directly, found nothing, and threw on the
/// first sign-in.
/// </summary>
public sealed class JwtSettings
{
    private JwtSettings(string issuer, string audience, string signingKey)
    {
        Issuer = issuer;
        Audience = audience;
        SigningKey = signingKey;
    }

    public string Issuer { get; }

    public string Audience { get; }

    /// <summary>Symmetric signing key. Never logged; the destructuring policy masks it.</summary>
    public string SigningKey { get; }

    /// <summary>
    /// Reads the settings from configuration.
    ///
    /// Outside Development a missing key is fatal, because the alternative is an application
    /// that signs tokens with a value an attacker could guess. In Development an ephemeral key
    /// is generated so a fresh clone runs; it changes on restart, which invalidates previously
    /// issued dev tokens and is the correct trade for never shipping a checked-in secret.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
    /// <returns>Resolved settings.</returns>
    public static JwtSettings Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var section = configuration.GetSection("Jwt");
        var signingKey = section["SigningKey"];

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Jwt:SigningKey is not configured. Set it through user-secrets or the "
                    + "UPBAZAAR_Jwt__SigningKey environment variable.");
            }

            signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        }

        return new JwtSettings(
            section["Issuer"] ?? "https://upbazaar.dev",
            section["Audience"] ?? "upbazaar-api",
            signingKey);
    }
}
