using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UPBazaar.Infrastructure;
using UPBazaar.Modules.Shipping.Application;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Shipping;

/// <summary>
/// Consignments, courier bookings, tracking and delivery status, through Shiprocket.
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
    /// Registers the module and picks its courier gateway the way Payments picks its payment
    /// gateway: the fake in Development and Testing when asked for or when no credentials are set,
    /// Shiprocket wherever they are, and otherwise none, which turns courier booking off.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddShippingModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddModule<ShippingModule>();

        var section = configuration.GetSection(ShiprocketOptions.SectionName);
        services.Configure<ShiprocketOptions>(section);

        var options = section.Get<ShiprocketOptions>() ?? new ShiprocketOptions();
        var mayFake = environment.IsDevelopment() || environment.IsEnvironment("Testing");

        if (mayFake && (options.UseFake || !options.IsConfigured))
        {
            services.AddSingleton<ICourierGateway, FakeCourierGateway>();
        }
        else if (options.IsConfigured)
        {
            services.AddSingleton<ShiprocketTokenCache>();
            services.AddHttpClient<ICourierGateway, ShiprocketGateway>(client =>
            {
                client.BaseAddress = new Uri("https://apiv2.shiprocket.in/v1/external/");
                client.Timeout = TimeSpan.FromSeconds(20);
            });
        }
        else
        {
            services.AddSingleton<ICourierGateway, UnconfiguredCourierGateway>();
        }

        services.AddScoped<ParcelPlanner>();

        return services;
    }
}
