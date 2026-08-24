using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Modules;
using UPBazaar.Modules.Catalog.Contracts;
using UPBazaar.Modules.Catalog.Services;

namespace UPBazaar.Modules.Catalog;

public static class CatalogModule
{
    /// <summary>
    /// Registers the catalog module: its schema, handlers, validators, and the contract
    /// implementations other modules resolve.
    /// </summary>
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddModule<CatalogModuleSchema>();

        services.AddScoped<IProductCatalog, ProductCatalogService>();
        services.AddScoped<IStockReservations, ProductCatalogService>();

        return services;
    }
}
