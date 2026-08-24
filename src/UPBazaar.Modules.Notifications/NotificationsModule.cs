using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure;
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
    /// Registers the module's schema, validators and handlers. Add module-specific services
    /// here as the module grows; everything discovered by convention needs no change.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services) =>
        services.AddModule<NotificationsModule>();
}
