using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.ExternalServices.Shipping;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Ordering.Contracts.Events;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Shipping.Application.Shipments;

internal sealed class CancelShipmentOnOrderCancelled(
    UPBazaarDbContext dbContext,
    IShippingProvider shippingProvider) : IDomainEventHandler<OrderCancelledDomainEvent>
{
    public async Task HandleAsync(
        OrderCancelledDomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var shipment = await dbContext.Set<Shipment>()
            .FirstOrDefaultAsync(s => s.OrderId == domainEvent.OrderId, cancellationToken);

        if (shipment is null || shipment.Status == ShipmentStatus.Cancelled)
        {
            return;
        }

        if (shipment.Cancel().IsFailure)
        {
            return;
        }

        if (shipment.AwbNumber is not null)
        {
            await shippingProvider.CancelAsync(shipment.AwbNumber, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
