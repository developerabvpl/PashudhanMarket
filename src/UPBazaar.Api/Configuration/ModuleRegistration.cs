using UPBazaar.Modules.Academy;
using UPBazaar.Modules.Cart;
using UPBazaar.Modules.Catalog;
using UPBazaar.Modules.Cms;
using UPBazaar.Modules.Crm;
using UPBazaar.Modules.Identity;
using UPBazaar.Modules.Inventory;
using UPBazaar.Modules.Notifications;
using UPBazaar.Modules.Orders;
using UPBazaar.Modules.Payments;
using UPBazaar.Modules.Promotions;
using UPBazaar.Modules.Reporting;
using UPBazaar.Modules.Reviews;
using UPBazaar.Modules.Sellers;
using UPBazaar.Modules.Settlements;
using UPBazaar.Modules.Shipping;

namespace UPBazaar.Api.Configuration;

/// <summary>
/// The one list of modules in the system.
///
/// It is explicit rather than assembly-scanned so that the set of modules a build contains is
/// a decision in source control, visible in a diff, and identical between the API host, the
/// design-time migration factory and the tests.
/// </summary>
public static class ModuleRegistration
{
    /// <summary>Registers every module with the host.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration, for modules that bind options.</param>
    /// <param name="environment">Host environment, for modules that stub differently in Development.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddModules(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddIdentityModule(configuration)
            .AddSellersModule(configuration)
            .AddCatalogModule(configuration)
            .AddInventoryModule()
            .AddCartModule()
            .AddOrdersModule()
            .AddPaymentsModule(configuration, environment)
            .AddShippingModule(configuration, environment)
            .AddSettlementsModule()
            .AddPromotionsModule()
            .AddReviewsModule()
            .AddCrmModule()
            .AddAcademyModule()
            .AddCmsModule()
            .AddNotificationsModule(environment)
            .AddReportingModule();
    }
}
