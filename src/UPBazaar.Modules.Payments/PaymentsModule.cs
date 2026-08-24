using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Modules;
using UPBazaar.Modules.Payments.Contracts;
using UPBazaar.Modules.Payments.Services;

namespace UPBazaar.Modules.Payments;

public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services)
    {
        services.AddModule<PaymentsModuleSchema>();
        services.AddScoped<IPaymentInitiation, PaymentInitiationService>();

        return services;
    }
}
