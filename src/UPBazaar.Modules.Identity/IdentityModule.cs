using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
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
    /// Registers the module's schema, validators and handlers. Add module-specific services
    /// here as the module grows; everything discovered by convention needs no change.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services) =>
        services.AddModule<IdentityModule>();
}
