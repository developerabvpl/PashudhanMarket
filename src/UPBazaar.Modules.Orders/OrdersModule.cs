using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.Modules.Orders.Application;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Orders.Services;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Orders;

/// <summary>
/// Checkout, order lifecycle and cancellations. Owns the priced snapshot an order is judged against.
/// </summary>
public sealed class OrdersModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "orders";

    /// <inheritdoc />
    public string Name => "Orders";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(OrdersModule).Assembly;
}

/// <summary>Registration entry point for the Orders module.</summary>
public static class OrdersModuleExtensions
{
    /// <summary>Registers the module's schema, handlers, validators and services.</summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddOrdersModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddModule<OrdersModule>();

        services.AddScoped<OrderTransaction>();
        services.AddScoped<OrderReader>();
        services.AddScoped<OrderStock>();
        services.AddScoped<OrderCanceller>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<UnpaidOrderExpiryJob>();

        return services;
    }
}
