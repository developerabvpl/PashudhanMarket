using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Promotions.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Promotions.Application;

/// <summary>
/// An order was cancelled outright - nothing in it reached the buyer - so its coupon use is given
/// back: the buyer may use the code again, and it no longer counts against the coupon's limit.
/// Cancelling part of an order leaves the use, and the discount, as they were.
/// </summary>
internal sealed class OrderCancelledCouponHandler(UPBazaarDbContext dbContext, IClock clock)
    : IDomainEventHandler<OrderCancelledDomainEvent>
{
    public async Task HandleAsync(OrderCancelledDomainEvent e, CancellationToken cancellationToken)
    {
        var redemption = await dbContext.Set<CouponRedemption>()
            .FirstOrDefaultAsync(r => r.OrderId == e.OrderId && r.ReleasedAtUtc == null, cancellationToken);

        if (redemption is null)
        {
            return;
        }

        var coupon = await dbContext.Set<Coupon>().FirstAsync(c => c.Id == redemption.CouponId, cancellationToken);

        redemption.Release(clock.UtcNow);
        coupon.GiveBackUse();

        // A concurrency clash with a checkout using the same coupon propagates, and the outbox retries.
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
