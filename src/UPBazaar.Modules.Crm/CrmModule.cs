using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Crm;

/// <summary>
/// Customer records, support tickets and interaction history.
/// </summary>
public sealed class CrmModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "crm";

    /// <inheritdoc />
    public string Name => "Crm";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(CrmModule).Assembly;
}

/// <summary>Registration entry point for the Crm module.</summary>
public static class CrmModuleExtensions
{
    /// <summary>
    /// Registers the module's schema, validators and handlers. Add module-specific services
    /// here as the module grows; everything discovered by convention needs no change.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddCrmModule(this IServiceCollection services) =>
        services.AddModule<CrmModule>();
}
