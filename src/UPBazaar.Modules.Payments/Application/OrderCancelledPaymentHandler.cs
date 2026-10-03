using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Payments.Application;

/// <summary>
/// An order ended cancelled. If its payment had confirmed it - the buyer or staff cancelled it, or
/// every parcel came back undelivered - the payment says so, so staff do not read "order confirmed"
/// against money that is going back; the refunds themselves come part by part. If the buyer never
/// paid - the time to pay ran out, or they cancelled first - the payment is abandoned, so staff do
/// not read "awaiting buyer" against an order nobody can pay for any more.
/// </summary>
internal sealed class OrderCancelledPaymentHandler(UPBazaarDbContext dbContext)
    : IDomainEventHandler<OrderCancelledDomainEvent>
{
    public async Task HandleAsync(OrderCancelledDomainEvent e, CancellationToken cancellationToken)
    {
        // Paid and still pending is money on its way to the order: the cancelled order refuses it,
        // and that records the refund. Everything else either confirmed the order or was never paid.
        var payments = await dbContext.Set<Payment>()
            .Where(p => p.OrderId == e.OrderId
                && ((p.Status == PaymentStatus.Paid && p.OrderOutcome == OrderOutcome.Confirmed)
                    || p.Status == PaymentStatus.Created))
            .ToListAsync(cancellationToken);

        if (payments.Count == 0)
        {
            return;
        }

        foreach (var payment in payments)
        {
            payment.RecordOrderCancelled();
        }

        // A clash with a refund being recorded on the same payment - or with the buyer paying at
        // this very moment - propagates, and the outbox retries against what was saved.
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
