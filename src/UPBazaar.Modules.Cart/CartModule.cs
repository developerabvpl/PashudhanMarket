using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Cart;

/// <summary>
/// Buyer carts and saved items, including guest carts that later merge into an account.
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
    /// <summary>
    /// Registers the module's schema, validators and handlers. Add module-specific services
    /// here as the module grows; everything discovered by convention needs no change.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddCartModule(this IServiceCollection services) =>
        services.AddModule<CartModule>();
}
