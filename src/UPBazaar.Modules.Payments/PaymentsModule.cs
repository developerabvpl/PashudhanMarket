using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Payments;

/// <summary>
/// Payment intents, captures, refunds and gateway webhooks.
/// </summary>
public sealed class PaymentsModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "payments";

    /// <inheritdoc />
    public string Name => "Payments";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(PaymentsModule).Assembly;
}

/// <summary>Registration entry point for the Payments module.</summary>
public static class PaymentsModuleExtensions
{
    /// <summary>
    /// Registers the module's schema, validators and handlers. Add module-specific services
    /// here as the module grows; everything discovered by convention needs no change.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services) =>
        services.AddModule<PaymentsModule>();
}
