using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Ordering.Domain;
using UPBazaar.Modules.Payments.Contracts.Events;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Ordering.Application.Orders;

/// <summary>
/// Reacts to a capture in the Payments module. This runs off the shared outbox, which is why
/// Ordering never calls into Payments to ask whether money arrived.
/// </summary>
internal sealed class MarkOrderPaidHandler(
    UPBazaarDbContext dbContext,
    ILogger<MarkOrderPaidHandler> logger) : IDomainEventHandler<PaymentCapturedDomainEvent>
{
    public async Task HandleAsync(
        PaymentCapturedDomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Set<Order>()
            .FirstOrDefaultAsync(o => o.PublicId == domainEvent.OrderId, cancellationToken);

        if (order is null)
        {
            logger.LogWarning(
                "Payment {PaymentId} captured for unknown order {OrderId}",
                domainEvent.PaymentId,
                domainEvent.OrderId);

            return;
        }

        var paid = order.MarkPaid(domainEvent.PaymentId);

        if (paid.IsFailure)
        {
            logger.LogWarning(
                "Order {OrderNumber} could not be marked paid: {ErrorCode}",
                order.OrderNumber,
                paid.Error.Code);

            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
