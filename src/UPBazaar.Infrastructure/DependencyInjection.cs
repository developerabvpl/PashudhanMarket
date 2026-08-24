using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Correlation;
using UPBazaar.Infrastructure.Identity;
using UPBazaar.Infrastructure.Messaging;
using UPBazaar.Infrastructure.Outbox;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Infrastructure.Persistence.Interceptors;
using UPBazaar.Infrastructure.Time;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Infrastructure;

/// <summary>Composition root for everything that is not a module.</summary>
public static class DependencyInjection
{
    /// <summary>Name of the connection string in configuration.</summary>
    public const string ConnectionStringName = "UPBazaar";

    /// <summary>
    /// Registers persistence, the dispatcher, and the ambient services modules depend on.
    /// Call before <c>AddModule</c>, so a module can override anything it needs to.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpContextAccessor();

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ICorrelationContext, CorrelationContext>();
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped<OutboxProcessor>();

        services.AddPersistence(configuration);

        return services;
    }

    /// <summary>
    /// Wires one module into the host: its schema declaration, its validators, and every
    /// command, query and domain event handler it declares.
    /// </summary>
    /// <typeparam name="TModule">The module's <see cref="IModule"/> implementation.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddModule<TModule>(this IServiceCollection services)
        where TModule : class, IModule, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        var module = new TModule();

        services.AddSingleton<IModule>(module);
        services.AddValidatorsFromAssembly(module.Assembly, includeInternalTypes: true);

        return services.AddHandlersFrom(module.Assembly);
    }

    private static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<AuditInterceptor>();
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

            // Order matters: audit rows describe the state change, and the outbox drains the
            // events that change raised. Both must land in the caller's transaction.
            options.AddInterceptors(
                provider.GetRequiredService<AuditInterceptor>(),
                provider.GetRequiredService<OutboxInterceptor>());
        });

        return services;
    }

    /// <summary>
    /// Scans an assembly for handler implementations and registers each against every handler
    /// interface it closes. A module therefore declares a handler simply by writing one.
    /// </summary>
    private static IServiceCollection AddHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        Type[] handlerInterfaces =
        [
            typeof(ICommandHandler<>),
            typeof(ICommandHandler<,>),
            typeof(IQueryHandler<,>),
            typeof(IDomainEventHandler<>),
        ];

        var implementations = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

        foreach (var implementation in implementations)
        {
            var contracts = implementation.GetInterfaces()
                .Where(i => i.IsGenericType && handlerInterfaces.Contains(i.GetGenericTypeDefinition()));

            foreach (var contract in contracts)
            {
                services.AddScoped(contract, implementation);
            }
        }

        return services;
    }
}
