using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.Modules.Settlements.Application;
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
    /// <summary>Registers the module's schema, validators, handlers and the weekly payout run.</summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSettlementsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddModule<SettlementsModule>();

        services.AddScoped<PolicyReader>();
        services.AddScoped<PayoutRunner>();
        services.AddScoped<PayoutRunJob>();

        return services;
    }
}
