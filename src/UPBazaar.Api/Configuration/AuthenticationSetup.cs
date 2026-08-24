using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using UPBazaar.Infrastructure.Identity;

namespace UPBazaar.Api.Configuration;

/// <summary>
/// Bearer token validation.
///
/// The Identity module issues the tokens; this side only validates them. Both read the same
/// singleton <see cref="JwtSettings"/>, so the key used to sign and the key used to verify are
/// the same object rather than two independent reads of configuration.
/// </summary>
public static class AuthenticationSetup
{
    /// <summary>Adds JWT bearer authentication.</summary>
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

        var settings = JwtSettings.Resolve(configuration, environment);
        services.AddSingleton(settings);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = settings.Issuer,
                ValidAudience = settings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                ClockSkew = TimeSpan.FromSeconds(30),
            });

        return services;
    }
}
