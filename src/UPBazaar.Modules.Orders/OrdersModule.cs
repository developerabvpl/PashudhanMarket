using System.Reflection;
using Microsoft.Extensions.Configuration;
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
    /// <param name="configuration">Application configuration, for <see cref="OrdersModuleOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddOrdersModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModule<OrdersModule>();

        services.AddOptions<OrdersModuleOptions>()
            .Bind(configuration.GetSection(OrdersModuleOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<OrderTransaction>();
        services.AddScoped<OrderReader>();
        services.AddScoped<OrderStock>();
        services.AddScoped<OrderCanceller>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IOrderFulfilmentService, OrderFulfilmentService>();
        services.AddScoped<IOrderDirectory, OrderDirectory>();
        services.AddScoped<IDeliveryCharges, DeliveryCharges>();
        services.AddScoped<UnpaidOrderExpiryJob>();

        return services;
    }
}
