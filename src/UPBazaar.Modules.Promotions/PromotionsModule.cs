using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.Modules.Promotions.Contracts;
using UPBazaar.Modules.Promotions.Services;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Promotions;

/// <summary>
/// Coupon codes - the platform's and sellers' own - the campaigns sellers join, and the record of
/// which order used which.
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
    /// <summary>Registers the module's schema, handlers, and the coupon pricing checkout uses.</summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPromotionsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddModule<PromotionsModule>();
        services.AddScoped<ICouponPricing, CouponPricing>();

        return services;
    }
}
