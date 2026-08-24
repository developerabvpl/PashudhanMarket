using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Infrastructure.Modules;

/// <summary>
/// Wires a module into the host: its schema, its validators, and every command, query and
/// domain event handler it declares. Modules call this from their own Add*Module method.
/// </summary>
public static class ModuleRegistrationExtensions
{
    private static readonly Type[] HandlerInterfaces =
    [
        typeof(ICommandHandler<>),
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
        typeof(IDomainEventHandler<>),
    ];

    public static IServiceCollection AddModule<TSchema>(this IServiceCollection services)
        where TSchema : class, IModuleSchema, new()
    {
        var schema = new TSchema();

        services.AddSingleton<IModuleSchema>(schema);
        services.AddValidatorsFromAssembly(schema.Assembly, includeInternalTypes: true);

        return services.AddHandlersFrom(schema.Assembly);
    }

    private static IServiceCollection AddHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        var implementations = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

        foreach (var implementation in implementations)
        {
            var handled = implementation.GetInterfaces()
                .Where(i => i.IsGenericType && HandlerInterfaces.Contains(i.GetGenericTypeDefinition()));

            foreach (var contract in handled)
            {
                services.AddScoped(contract, implementation);
            }
        }

        return services;
    }
}
