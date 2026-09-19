using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.Modules.Catalog.Services;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Catalog;

/// <summary>
/// Products, categories and the attributes buyers browse and search by.
/// </summary>
public sealed class CatalogModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "catalog";

    /// <inheritdoc />
    public string Name => "Catalog";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(CatalogModule).Assembly;
}

/// <summary>Registration entry point for the Catalog module.</summary>
public static class CatalogModuleExtensions
{
    /// <summary>Registers the module's schema, handlers, validators and services.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModule<CatalogModule>();

        services.AddOptions<CatalogModuleOptions>()
            .Bind(configuration.GetSection(CatalogModuleOptions.SectionName));

        services.AddScoped<CatalogSeeder>();

        return services;
    }
}
