using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Payments.Application;

/// <summary>
/// A paid order ended cancelled after its payment confirmed it - the buyer or staff cancelled it,
/// or every parcel came back undelivered. The payment says so, so staff do not read "order
/// confirmed" against money that is going back. The refunds themselves come part by part.
/// </summary>
internal sealed class OrderCancelledPaymentHandler(UPBazaarDbContext dbContext)
    : IDomainEventHandler<OrderCancelledDomainEvent>
{
    public async Task HandleAsync(OrderCancelledDomainEvent e, CancellationToken cancellationToken)
    {
        var payment = await dbContext.Set<Payment>()
            .Where(p => p.OrderId == e.OrderId && p.Status == PaymentStatus.Paid && p.OrderOutcome == OrderOutcome.Confirmed)
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            return;
        }

        payment.RecordOrderCancelled();

        // A clash with a refund being recorded on the same payment propagates, and the outbox retries.
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
