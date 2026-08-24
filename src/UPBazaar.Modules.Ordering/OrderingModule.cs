using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Modules;

namespace UPBazaar.Modules.Ordering;

public static class OrderingModule
{
    public static IServiceCollection AddOrderingModule(this IServiceCollection services) =>
        services.AddModule<OrderingModuleSchema>();
}
