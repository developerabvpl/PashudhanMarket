using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Modules;

namespace UPBazaar.Modules.Shipping;

public static class ShippingModule
{
    public static IServiceCollection AddShippingModule(this IServiceCollection services) =>
        services.AddModule<ShippingModuleSchema>();
}
