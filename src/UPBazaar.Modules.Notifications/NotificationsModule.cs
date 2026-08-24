using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UPBazaar.Infrastructure;
using UPBazaar.Modules.Notifications.Contracts;
using UPBazaar.Modules.Notifications.Delivery;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Modules.Notifications;

/// <summary>
/// Templates and delivery of SMS, email and push, plus per-user preferences.
/// </summary>
public sealed class NotificationsModule : IModule
{
    /// <summary>SQL schema this module owns. No other module reads or writes inside it.</summary>
    public const string SchemaName = "notifications";

    /// <inheritdoc />
    public string Name => "Notifications";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <inheritdoc />
    public Assembly Assembly => typeof(NotificationsModule).Assembly;
}

/// <summary>Registration entry point for the Notifications module.</summary>
public static class NotificationsModuleExtensions
{
    /// <summary>
    /// Registers the module. Delivery is stubbed: Development gets senders that log the
    /// message, and every other environment gets senders that throw, so a host without a real
    /// provider fails at the first send rather than silently dropping a one-time code.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="environment">Host environment.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddModule<NotificationsModule>();

        // Testing counts as development here: the integration suite asserts on the code it
        // sends, and a throwing sender would make the OTP tests untestable.
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<ISmsSender, DevSmsSender>();
            services.AddSingleton<IEmailSender, DevEmailSender>();
        }
        else
        {
            services.AddSingleton<ISmsSender, UnconfiguredSmsSender>();
            services.AddSingleton<IEmailSender, UnconfiguredEmailSender>();
        }

        return services;
    }
}
