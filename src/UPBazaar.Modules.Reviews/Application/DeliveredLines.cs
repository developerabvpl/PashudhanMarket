using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Reviews.Domain;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Reviews.Application;

/// <summary>
/// A parcel was delivered: every product in it may now be reviewed by its buyer.
///
/// The event names the parcel but not the buyer or the products, so the parcel is read back from
/// Orders. The outbox may deliver the event twice; the second finds the lines already there.
/// </summary>
internal sealed class OrderPartDeliveredReviewHandler(UPBazaarDbContext dbContext, IOrderFulfilmentService orders)
    : IDomainEventHandler<OrderPartDeliveredDomainEvent>
{
    public async Task HandleAsync(OrderPartDeliveredDomainEvent e, CancellationToken cancellationToken)
    {
        if (await dbContext.Set<ReviewableLine>().AnyAsync(x => x.OrderPartId == e.PartId, cancellationToken))
        {
            return;
        }

        var part = await orders.GetPartAsync(e.OrderId, e.PartId, cancellationToken);

        if (part.IsFailure)
        {
            // Orders just told us the parcel exists. If it cannot find it now, something is wrong
            // enough that the outbox should retry and, failing that, show the error.
            throw new InvalidOperationException(
                $"Delivered parcel {e.PartId} of order {e.Number} could not be read: {part.Error.Code}.");
        }

        // A parcel can hold the same product on more than one line; it is one thing to review.
        foreach (var line in part.Value.Lines.DistinctBy(l => l.ProductId))
        {
            dbContext.Set<ReviewableLine>().Add(ReviewableLine.Record(
                part.Value.BuyerId,
                line.ProductId,
                part.Value.SellerId,
                line.Name,
                e.OrderId,
                e.PartId,
                e.DeliveredAtUtc));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
