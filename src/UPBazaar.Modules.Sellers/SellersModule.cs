using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Sellers;

/// <summary>
/// Seller onboarding, KYC documents, payout details and shop profiles.
/// </summary>
public sealed class SellersModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "sellers";

    /// <inheritdoc />
    public string Name => "Sellers";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(SellersModule).Assembly;
}

/// <summary>Registration entry point for the Sellers module.</summary>
public static class SellersModuleExtensions
{
    /// <summary>
    /// Registers the module's schema, validators and handlers. Add module-specific services
    /// here as the module grows; everything discovered by convention needs no change.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSellersModule(this IServiceCollection services) =>
        services.AddModule<SellersModule>();
}
