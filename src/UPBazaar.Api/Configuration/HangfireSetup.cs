using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using UPBazaar.Infrastructure;
using UPBazaar.Infrastructure.Outbox;
using UPBazaar.Modules.Inventory.Services;

namespace UPBazaar.Api.Configuration;

/// <summary>Background processing: storage, server, dashboard and the recurring jobs.</summary>
public static class HangfireSetup
{
    /// <summary>Authorization policy the /jobs dashboard requires.</summary>
    public const string DashboardPolicy = "hangfire.dashboard";

    /// <summary>Permission that policy looks for.</summary>
    public const string DashboardPermission = "platform.jobs.view";

    /// <summary>
    /// Registers Hangfire. Switchable so tests and design-time tooling do not need a storage
    /// connection: <c>Hangfire:Enabled=false</c> leaves the whole thing out.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.GetValue("Hangfire:Enabled", defaultValue: true))
        {
            return services;
        }

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(
                configuration.GetConnectionString(DependencyInjection.ConnectionStringName),
                new SqlServerStorageOptions
                {
                    SchemaName = "hangfire",
                    PrepareSchemaIfNecessary = true,
                    QueuePollInterval = TimeSpan.FromSeconds(5),
                    DisableGlobalLocks = true,
                }));

        if (configuration.GetValue("Hangfire:EnableServer", defaultValue: true))
        {
            services.AddHangfireServer(options => options.WorkerCount = 4);
        }

        return services;
    }

    /// <summary>
    /// Maps the dashboard at /jobs behind an ASP.NET Core authorization policy.
    ///
    /// The policy is enforced by the endpoint pipeline rather than by a Hangfire dashboard
    /// filter, so the dashboard obeys the same authentication and permission rules as every
    /// other endpoint instead of having a parallel scheme of its own.
    /// </summary>
    /// <param name="app">Web application.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapJobsDashboard(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Configuration.GetValue("Hangfire:Enabled", defaultValue: true))
        {
            return app;
        }

        app.MapHangfireDashboard("/jobs", new DashboardOptions
        {
            DashboardTitle = "UP Bazaar jobs",
            // Endpoint authorization already gated this; an empty filter list avoids
            // Hangfire's default of "local requests only", which would block a deployed
            // instance even for an authorised operator.
            Authorization = [],
            IgnoreAntiforgeryToken = false,
        }).RequireAuthorization(DashboardPolicy);

        return app;
    }

    /// <summary>Registers the recurring jobs the platform depends on.</summary>
    /// <param name="app">Web application.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication ScheduleRecurringJobs(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Configuration.GetValue("Hangfire:Enabled", defaultValue: true))
        {
            return app;
        }

        var recurringJobs = app.Services.GetService<IRecurringJobManager>();

        recurringJobs?.AddOrUpdate<OutboxProcessor>(
            OutboxProcessor.RecurringJobId,
            processor => processor.ProcessAsync(CancellationToken.None),
            "*/1 * * * *");

        // Every minute, so an abandoned checkout keeps stock off sale for at most a minute past
        // its hold time.
        recurringJobs?.AddOrUpdate<ReservationExpiryJob>(
            ReservationExpiryJob.RecurringJobId,
            job => job.ExpireAsync(CancellationToken.None),
            "*/1 * * * *");

        return app;
    }
}
