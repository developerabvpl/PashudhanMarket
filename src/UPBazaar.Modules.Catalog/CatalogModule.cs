using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Catalog;

/// <summary>
/// Products, variants, categories and the attributes buyers browse and search by.
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
    /// <summary>
    /// Registers the module's schema, validators and handlers. Add module-specific services
    /// here as the module grows; everything discovered by convention needs no change.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddCatalogModule(this IServiceCollection services) =>
        services.AddModule<CatalogModule>();
}
