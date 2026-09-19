using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.Modules.Cart.Application;
using UPBazaar.Modules.Cart.Contracts;
using UPBazaar.Modules.Cart.Services;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Cart;

/// <summary>
/// Signed-in buyers' carts. A guest's basket lives in the browser and is merged in at sign-in.
/// </summary>
public sealed class CartModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "cart";

    /// <inheritdoc />
    public string Name => "Cart";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(CartModule).Assembly;
}

/// <summary>Registration entry point for the Cart module.</summary>
public static class CartModuleExtensions
{
    /// <summary>Registers the module's schema, handlers, validators and services.</summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddCartModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddModule<CartModule>();

        services.AddScoped<CartReader>();
        services.AddScoped<CartWriter>();
        services.AddScoped<ICartService, CartService>();

        return services;
    }
}
