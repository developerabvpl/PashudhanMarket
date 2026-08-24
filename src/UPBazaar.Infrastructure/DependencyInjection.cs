using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure.Authorization;
using UPBazaar.Infrastructure.ExternalServices;
using UPBazaar.Infrastructure.ExternalServices.Notifications;
using UPBazaar.Infrastructure.ExternalServices.Payments;
using UPBazaar.Infrastructure.ExternalServices.Shipping;
using UPBazaar.Infrastructure.ExternalServices.Storage;
using UPBazaar.Infrastructure.Identity;
using UPBazaar.Infrastructure.Messaging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Infrastructure.Persistence.Idempotency;
using UPBazaar.Infrastructure.Persistence.Interceptors;
using UPBazaar.Infrastructure.Time;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "UPBazaar";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        services.AddPersistence(configuration);
        services.AddPermissionAuthorization();
        services.AddExternalServices(configuration);

        services.AddScoped<OutboxProcessor>();

        return services;
    }

    private static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<OutboxInterceptor>();

        services.AddDbContext<UPBazaarDbContext>((provider, options) =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString(ConnectionStringName),
                sql =>
                {
                    sql.MigrationsAssembly(typeof(UPBazaarDbContext).Assembly.GetName().Name);
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", UPBazaarDbContext.SharedSchema);
                    sql.EnableRetryOnFailure();
                });

            options.AddInterceptors(
                provider.GetRequiredService<AuditableEntityInterceptor>(),
                provider.GetRequiredService<OutboxInterceptor>());
        });

        services.AddHealthChecks().AddDbContextCheck<UPBazaarDbContext>("database");

        return services;
    }

    private static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // Nothing is reachable without an explicit [Authorize] or [AllowAnonymous].
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    private static IServiceCollection AddExternalServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ExternalServicesOptions>()
            .Bind(configuration.GetSection(ExternalServicesOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration
            .GetSection(ExternalServicesOptions.SectionName)
            .Get<ExternalServicesOptions>() ?? new ExternalServicesOptions();

        if (!options.UseSandbox)
        {
            throw new NotSupportedException(
                "Live Razorpay and Shiprocket adapters are not implemented yet. Implement "
                + "IPaymentGateway and IShippingProvider against the provider APIs, register them "
                + "here, or set ExternalServices:UseSandbox to true.");
        }

        services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        services.AddSingleton<IShippingProvider, FakeShippingProvider>();
        services.AddSingleton<ISmsSender, FakeSmsSender>();
        services.AddSingleton<IEmailSender, FakeEmailSender>();
        services.AddSingleton<IBlobStorage, FakeBlobStorage>();

        return services;
    }
}
