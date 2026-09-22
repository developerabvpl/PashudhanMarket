using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.Modules.Identity.Contracts;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.Modules.Identity.Services;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Identity;

/// <summary>
/// Accounts, sign-in, roles and the permission claims every other module authorises against.
/// </summary>
public sealed class IdentityModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "identity";

    /// <inheritdoc />
    public string Name => "Identity";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(IdentityModule).Assembly;
}

/// <summary>Registration entry point for the Identity module.</summary>
public static class IdentityModuleExtensions
{
    /// <summary>
    /// Registers the module's schema, handlers, validators and services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModule<IdentityModule>();

        services.AddOptions<IdentityModuleOptions>()
            .Bind(configuration.GetSection(IdentityModuleOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ASP.NET Identity's hasher, without the rest of the Identity stack: PBKDF2 with a
        // versioned format, so raising the work factor later re-hashes on next sign-in rather
        // than invalidating every password.
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        services.AddSingleton<TotpService>();
        services.AddScoped<TokenService>();
        services.AddScoped<LoginAuditWriter>();
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IUserRoles, UserRoles>();

        return services;
    }
}
