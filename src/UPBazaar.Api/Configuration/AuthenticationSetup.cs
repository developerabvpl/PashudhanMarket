using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace UPBazaar.Api.Configuration;

/// <summary>
/// Bearer token validation.
///
/// The Identity module does not issue tokens yet, so this validates against a symmetric key.
/// When issuance lands, only the key material changes: permissions already arrive as claims,
/// and the authorization policies read them from there.
/// </summary>
public static class AuthenticationSetup
{
    /// <summary>
    /// Adds JWT bearer authentication. The signing key comes from user-secrets or the
    /// environment; Development falls back to an ephemeral key so a fresh clone still runs.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var jwt = configuration.GetSection("Jwt");
        var signingKey = jwt["SigningKey"];

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Jwt:SigningKey is not configured. Set it through user-secrets or the "
                    + "UPBAZAAR_Jwt__SigningKey environment variable.");
            }

            // Ephemeral: every restart invalidates previously issued dev tokens, which is the
            // correct trade for never shipping a checked-in key.
            signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt["Issuer"],
                ValidAudience = jwt["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                ClockSkew = TimeSpan.FromSeconds(30),
            });

        return services;
    }
}
