using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.Modules.Inventory.Application;
using UPBazaar.Modules.Inventory.Contracts;
using UPBazaar.Modules.Inventory.Services;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Inventory;

/// <summary>
/// Stock on hand, reservations and warehouse movements, kept apart from the catalogue so pricing and availability can scale separately.
/// </summary>
public sealed class InventoryModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "inventory";

    /// <inheritdoc />
    public string Name => "Inventory";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(InventoryModule).Assembly;
}

/// <summary>Registration entry point for the Inventory module.</summary>
public static class InventoryModuleExtensions
{
    /// <summary>Registers the module's schema, handlers, validators and services.</summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddModule<InventoryModule>();

        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<StockWriter>();
        services.AddScoped<ReservationExpiryJob>();

        return services;
    }
}
