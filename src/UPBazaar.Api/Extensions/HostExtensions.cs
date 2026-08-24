using System.Security.Cryptography;
using System.Text;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.IdentityModel.Tokens;
using UPBazaar.Infrastructure;
using UPBazaar.Infrastructure.Messaging;

namespace UPBazaar.Api.Extensions;

public static class HostExtensions
{
    /// <summary>
    /// Symmetric bearer validation. The signing key comes from user-secrets or the environment;
    /// Development falls back to an ephemeral key so a fresh clone still runs.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var jwt = configuration.GetSection("Jwt");
        var signingKey = jwt["SigningKey"];

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Jwt:SigningKey is not configured. Set it through user-secrets or the "
                    + "UPBAZAAR_Jwt__SigningKey environment variable.");
            }

            signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt["Issuer"],
                    ValidAudience = jwt["Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        return services;
    }

    /// <summary>
    /// Hangfire runs the outbox. It is switchable so tests and design-time tooling do not need
    /// a storage connection.
    /// </summary>
    public static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
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
                }));

        if (configuration.GetValue("Hangfire:EnableServer", defaultValue: true))
        {
            services.AddHangfireServer(options => options.WorkerCount = 4);
        }

        return services;
    }

    /// <summary>Registers the recurring jobs the platform depends on.</summary>
    public static WebApplication ScheduleRecurringJobs(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Hangfire:Enabled", defaultValue: true))
        {
            return app;
        }

        var recurringJobs = app.Services.GetService<IRecurringJobManager>();

        recurringJobs?.AddOrUpdate<OutboxProcessor>(
            OutboxProcessor.RecurringJobId,
            processor => processor.ProcessAsync(CancellationToken.None),
            "*/1 * * * *");

        return app;
    }

    /// <summary>
    /// Controllers live inside their modules, so each module assembly is registered as an
    /// application part rather than relying on discovery.
    /// </summary>
    public static IMvcBuilder AddCatalogApplicationPart(this IMvcBuilder builder) =>
        builder.AddApplicationPart(typeof(Modules.Catalog.CatalogModuleSchema).Assembly);

    public static IMvcBuilder AddOrderingApplicationPart(this IMvcBuilder builder) =>
        builder.AddApplicationPart(typeof(Modules.Ordering.OrderingModuleSchema).Assembly);

    public static IMvcBuilder AddPaymentsApplicationPart(this IMvcBuilder builder) =>
        builder.AddApplicationPart(typeof(Modules.Payments.PaymentsModuleSchema).Assembly);

    public static IMvcBuilder AddShippingApplicationPart(this IMvcBuilder builder) =>
        builder.AddApplicationPart(typeof(Modules.Shipping.ShippingModuleSchema).Assembly);
}
