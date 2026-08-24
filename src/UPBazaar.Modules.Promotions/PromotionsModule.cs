using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Promotions;

/// <summary>
/// Coupons, campaigns and price rules, and the record of which order used which.
/// </summary>
public sealed class PromotionsModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "promotions";

    /// <inheritdoc />
    public string Name => "Promotions";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(PromotionsModule).Assembly;
}

/// <summary>Registration entry point for the Promotions module.</summary>
public static class PromotionsModuleExtensions
{
    /// <summary>
    /// Registers the module's schema, validators and handlers. Add module-specific services
    /// here as the module grows; everything discovered by convention needs no change.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPromotionsModule(this IServiceCollection services) =>
        services.AddModule<PromotionsModule>();
}
