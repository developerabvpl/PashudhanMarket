using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.Modules.Sellers.Services;
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
    /// <summary>Registers the module's schema, handlers, validators and services.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration, for the seed sellers.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSellersModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModule<SellersModule>();
        services.Configure<SellersModuleOptions>(configuration.GetSection(SellersModuleOptions.SectionName));

        services.AddScoped<ISellerDirectory, SellerDirectory>();
        services.AddScoped<SellersSeeder>();

        return services;
    }
}
