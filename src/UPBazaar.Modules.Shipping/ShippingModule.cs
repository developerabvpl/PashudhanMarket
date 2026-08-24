using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Shipping;

/// <summary>
/// Consignments, courier bookings, tracking and delivery status.
/// </summary>
public sealed class ShippingModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "shipping";

    /// <inheritdoc />
    public string Name => "Shipping";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(ShippingModule).Assembly;
}

/// <summary>Registration entry point for the Shipping module.</summary>
public static class ShippingModuleExtensions
{
    /// <summary>
    /// Registers the module's schema, validators and handlers. Add module-specific services
    /// here as the module grows; everything discovered by convention needs no change.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddShippingModule(this IServiceCollection services) =>
        services.AddModule<ShippingModule>();
}
