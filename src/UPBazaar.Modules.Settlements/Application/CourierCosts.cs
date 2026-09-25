using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Settlements.Domain;
using UPBazaar.Modules.Shipping.Contracts.Events;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Settlements.Application;

/// <summary>
/// A courier trip was charged: the seller pays it, out of their next payout.
///
/// All but one: collecting a return the buyer sent back only because they no longer wanted it is
/// the platform's cost, since nothing was wrong with what the seller sent. Every other trip - the
/// delivery, an RTO, a return of damaged or wrong goods - is the seller's.
///
/// The outbox may deliver the event twice; the charge's reference is recorded, and the second
/// finds it there.
/// </summary>
internal sealed class ShipmentChargedCourierCostHandler(UPBazaarDbContext dbContext, IClock clock)
    : IDomainEventHandler<ShipmentChargedDomainEvent>
{
    /// <summary>The return reason whose pickup the platform pays for.</summary>
    private const string ChangedMind = "NoLongerNeeded";

    public async Task HandleAsync(ShipmentChargedDomainEvent e, CancellationToken cancellationToken)
    {
        if (e.Trip == "ReturnPickup" && e.ReturnReason == ChangedMind)
        {
            return;
        }

        var reference = string.Create(CultureInfo.InvariantCulture, $"{e.ShipmentId:N}:{e.Trip}:{e.Sequence}");

        if (await dbContext.Set<Earning>().AnyAsync(x => x.Reference == reference, cancellationToken))
        {
            return;
        }

        dbContext.Set<Earning>().Add(Earning.ForCourierCost(
            e.SellerId, e.OrderId, e.OrderNumber, e.OrderPartId, e.Trip, e.Amount, e.Currency, clock.UtcNow, reference));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
