using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Settlements;

/// <summary>
/// What each seller is owed: commission, deductions, payout runs and reconciliation.
/// </summary>
public sealed class SettlementsModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "settlements";

    /// <inheritdoc />
    public string Name => "Settlements";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(SettlementsModule).Assembly;
}

/// <summary>Registration entry point for the Settlements module.</summary>
public static class SettlementsModuleExtensions
{
    /// <summary>
    /// Registers the module's schema, validators and handlers. Add module-specific services
    /// here as the module grows; everything discovered by convention needs no change.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSettlementsModule(this IServiceCollection services) =>
        services.AddModule<SettlementsModule>();
}
