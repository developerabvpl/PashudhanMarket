using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Shipping.Application;

/// <summary>
/// Calls the courier off when a part is cancelled before it is collected.
///
/// Orders only lets a part be cancelled before it ships, so the shipment is at most booked with a
/// pickup requested. If the carrier refuses the cancellation the exception is left to propagate
/// and the outbox retries, because a courier that turns up for a cancelled parcel collects goods
/// that have already been returned to stock.
/// </summary>
internal sealed partial class OrderPartCancelledShipmentHandler(
    UPBazaarDbContext dbContext,
    ICourierGateway courier,
    ILogger<OrderPartCancelledShipmentHandler> logger) : IDomainEventHandler<OrderPartCancelledDomainEvent>
{
    public async Task HandleAsync(OrderPartCancelledDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var shipment = await dbContext.Set<Shipment>()
            .FirstOrDefaultAsync(
                s => s.OrderPartId == domainEvent.PartId && s.Status != ShipmentStatus.Cancelled,
                cancellationToken);

        if (shipment is null)
        {
            return;
        }

        if (!shipment.CanCancel)
        {
            LogTooLate(logger, domainEvent.Number, shipment.Awb);

            return;
        }

        if (shipment.CarrierOrderId is { } carrierOrderId)
        {
            var cancelled = await courier.CancelAsync(carrierOrderId, cancellationToken);

            if (cancelled.IsFailure)
            {
                throw new InvalidOperationException(
                    $"The courier would not cancel shipment {shipment.PublicId} for order {domainEvent.Number}: {cancelled.Error.Code}");
            }
        }

        shipment.Cancel();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Order {Number} part was cancelled but its parcel {Awb} is already with the courier")]
    private static partial void LogTooLate(ILogger logger, string number, string? awb);
}
