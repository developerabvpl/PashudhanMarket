using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Reporting;

/// <summary>
/// Read models and scheduled extracts for operations and finance.
/// </summary>
public sealed class ReportingModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "reporting";

    /// <inheritdoc />
    public string Name => "Reporting";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(ReportingModule).Assembly;
}

/// <summary>Registration entry point for the Reporting module.</summary>
public static class ReportingModuleExtensions
{
    /// <summary>
    /// Registers the module's schema, validators and handlers. Add module-specific services
    /// here as the module grows; everything discovered by convention needs no change.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddReportingModule(this IServiceCollection services) =>
        services.AddModule<ReportingModule>();
}
