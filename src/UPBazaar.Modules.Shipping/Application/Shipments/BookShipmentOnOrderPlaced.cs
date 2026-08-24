using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.ExternalServices.Shipping;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Ordering.Contracts.Events;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Shipping.Application.Shipments;

/// <summary>
/// Books a consignment when Ordering places an order. Arrives via the shared outbox, so
/// Shipping stays unreachable from Ordering code and a failed booking retries on its own.
/// </summary>
internal sealed class BookShipmentOnOrderPlaced(
    UPBazaarDbContext dbContext,
    IShippingProvider shippingProvider,
    ILogger<BookShipmentOnOrderPlaced> logger) : IDomainEventHandler<OrderPlacedDomainEvent>
{
    /// <summary>Placeholder until seller pickup addresses are modelled.</summary>
    private const string DefaultPickupPostcode = "221001";

    public async Task HandleAsync(OrderPlacedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Set<Shipment>()
            .FirstOrDefaultAsync(s => s.OrderId == domainEvent.OrderId, cancellationToken);

        if (existing is not null)
        {
            logger.LogDebug(
                "Shipment already exists for order {OrderNumber}; outbox redelivery ignored",
                domainEvent.OrderNumber);

            return;
        }

        var shipment = Shipment.ForOrder(
            domainEvent.OrderId,
            domainEvent.OrderNumber,
            domainEvent.DeliveryPostcode);

        var booking = await shippingProvider.CreateShipmentAsync(
            new ShipmentBookingRequest(
                domainEvent.OrderNumber,
                domainEvent.OrderNumber,
                DefaultPickupPostcode,
                domainEvent.DeliveryPostcode,
                WeightKg: domainEvent.Lines.Sum(l => l.Quantity) * 0.5m,
                DeclaredValue: domainEvent.Total),
            cancellationToken);

        var booked = shipment.Book(booking.AwbNumber, booking.Courier, booking.ExpectedDeliveryUtc);

        if (booked.IsFailure)
        {
            logger.LogWarning(
                "Could not book shipment for order {OrderNumber}: {ErrorCode}",
                domainEvent.OrderNumber,
                booked.Error.Code);

            return;
        }

        dbContext.Set<Shipment>().Add(shipment);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
