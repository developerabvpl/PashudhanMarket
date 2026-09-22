using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UPBazaar.Infrastructure;
using UPBazaar.Modules.Payments.Application;
using UPBazaar.Modules.Payments.Gateway;
using UPBazaar.Modules.Payments.Services;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Payments;

/// <summary>
/// Online payment through Razorpay, and the record of refunds owed. Orders decides what is due;
/// this module takes it and tells Orders when it has.
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
    /// Registers the module and picks its gateway: the fake one in Development and Testing when
    /// asked for or when no keys are set, Razorpay wherever keys are set, and otherwise none, which
    /// switches online payment off rather than letting it fail at the buyer's last step.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPaymentsModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddModule<PaymentsModule>();

        var section = configuration.GetSection(RazorpayOptions.SectionName);
        services.Configure<RazorpayOptions>(section);

        var options = section.Get<RazorpayOptions>() ?? new RazorpayOptions();
        var mayFake = environment.IsDevelopment() || environment.IsEnvironment("Testing");

        if (mayFake && (options.UseFake || !options.IsConfigured))
        {
            services.AddSingleton<IPaymentGateway, FakeGateway>();
        }
        else if (options.IsConfigured)
        {
            services.AddHttpClient<IPaymentGateway, RazorpayGateway>(client =>
            {
                client.BaseAddress = new Uri("https://api.razorpay.com/v1/");
                client.Timeout = TimeSpan.FromSeconds(15);
            });
        }
        else
        {
            services.AddSingleton<IPaymentGateway, UnconfiguredGateway>();
        }

        services.AddScoped<PaymentSettler>();
        services.AddScoped<PartRefundRecorder>();
        services.AddScoped<PaymentSettlementJob>();

        return services;
    }
}
