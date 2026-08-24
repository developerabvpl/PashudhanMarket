using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Cms;

/// <summary>
/// Editorial content: pages, banners, menus and localised copy.
/// </summary>
public sealed class CmsModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "cms";

    /// <inheritdoc />
    public string Name => "Cms";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(CmsModule).Assembly;
}

/// <summary>Registration entry point for the Cms module.</summary>
public static class CmsModuleExtensions
{
    /// <summary>
    /// Registers the module's schema, validators and handlers. Add module-specific services
    /// here as the module grows; everything discovered by convention needs no change.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddCmsModule(this IServiceCollection services) =>
        services.AddModule<CmsModule>();
}
