using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Settlements.Domain;
using UPBazaar.Modules.Shipping.Contracts;
using UPBazaar.Modules.Shipping.Contracts.Events;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Settlements.Application;

/// <summary>
/// A parcel was delivered: the seller has earned from its goods - less any coupon discount they
/// bear; one the platform bears leaves them paid in full - and from its share of the
/// delivery charge if the buyer paid one, at today's rates, payable once the buyer's return
/// window closes. The outbox may deliver the event twice; the second finds both already there.
///
/// Paid in cash at the door, both also wait for the courier to pay that cash over - unless its
/// remittance was matched before this event arrived.
/// </summary>
internal sealed class OrderPartDeliveredEarningHandler(UPBazaarDbContext dbContext, PolicyReader policy, ICodCash codCash)
    : IDomainEventHandler<OrderPartDeliveredDomainEvent>
{
    public async Task HandleAsync(OrderPartDeliveredDomainEvent e, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Set<Earning>()
            .Where(x => x.OrderPartId == e.PartId)
            .Select(x => x.Kind)
            .ToListAsync(cancellationToken);

        var rates = await policy.RatesForAsync(e.SellerId, cancellationToken);
        var added = new List<Earning>();

        if (!existing.Contains(EarningKind.Sale))
        {
            added.Add(Earning.Create(
                e.SellerId, e.OrderId, e.Number, e.PartId, e.Subtotal - e.SellerDiscount, e.Currency, e.DeliveredAtUtc, e.ReturnWindowClosesAtUtc, rates));
        }

        if (e.DeliveryFee > 0 && !existing.Contains(EarningKind.Delivery))
        {
            added.Add(Earning.ForDelivery(
                e.SellerId, e.OrderId, e.Number, e.PartId, e.DeliveryFee, e.Currency, e.DeliveredAtUtc, e.ReturnWindowClosesAtUtc, rates));
        }

        if (added.Count > 0 && e.CashOnDelivery && !await codCash.IsCashInAsync(e.PartId, cancellationToken))
        {
            added.ForEach(earning => earning.AwaitCash());
        }

        dbContext.Set<Earning>().AddRange(added);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>A return was asked for: hold the earning until it is decided.</summary>
internal sealed class OrderPartReturnRequestedEarningHandler(UPBazaarDbContext dbContext)
    : IDomainEventHandler<OrderPartReturnRequestedDomainEvent>
{
    public Task HandleAsync(OrderPartReturnRequestedDomainEvent e, CancellationToken cancellationToken) =>
        EarningUpdate.ApplyAsync(dbContext, e.PartId, earning => earning.Hold(), cancellationToken);
}

/// <summary>The return was refused: the sale stands and the earning is payable again.</summary>
internal sealed class OrderPartReturnRejectedEarningHandler(UPBazaarDbContext dbContext)
    : IDomainEventHandler<OrderPartReturnRejectedDomainEvent>
{
    public Task HandleAsync(OrderPartReturnRejectedDomainEvent e, CancellationToken cancellationToken) =>
        EarningUpdate.ApplyAsync(dbContext, e.PartId, earning => earning.Release(), cancellationToken);
}

/// <summary>
/// The return was accepted: the goods go back to the seller and the buyer is refunded, so the
/// seller earns nothing from the sale. Cancelled now rather than when the parcel arrives, so a
/// return still in transit is never paid out.
/// </summary>
internal sealed class OrderPartReturnApprovedEarningHandler(UPBazaarDbContext dbContext)
    : IDomainEventHandler<OrderPartReturnApprovedDomainEvent>
{
    public Task HandleAsync(OrderPartReturnApprovedDomainEvent e, CancellationToken cancellationToken) =>
        EarningUpdate.ApplyAsync(dbContext, e.PartId, earning => earning.Cancel(), cancellationToken);
}

/// <summary>
/// Applies a return's change to a parcel's sale earning. The delivery earning is left alone: the
/// delivery was made whatever happens to the goods. A parcel with no earning - delivered before Settlements
/// existed - has nothing to change. A concurrency clash is left to propagate so the outbox retries.
/// </summary>
internal static class EarningUpdate
{
    public static async Task ApplyAsync(
        UPBazaarDbContext dbContext,
        Guid partId,
        Action<Earning> change,
        CancellationToken cancellationToken)
    {
        var earning = await dbContext.Set<Earning>()
            .FirstOrDefaultAsync(x => x.OrderPartId == partId && x.Kind == EarningKind.Sale, cancellationToken);

        if (earning is null)
        {
            return;
        }

        change(earning);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The rates in force: the platform policy, created with its defaults on first use.</summary>
internal sealed class PolicyReader(UPBazaarDbContext dbContext)
{
    public async Task<SettlementPolicy> GetAsync(CancellationToken cancellationToken)
    {
        var policy = await dbContext.Set<SettlementPolicy>().OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);

        if (policy is null)
        {
            policy = SettlementPolicy.CreateDefault();
            dbContext.Set<SettlementPolicy>().Add(policy);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return policy;
    }

    /// <summary>The seller's own commission if they have one, else the default; the taxes are the same for all.</summary>
    public async Task<EarningRates> RatesForAsync(Guid sellerId, CancellationToken cancellationToken)
    {
        var policy = await GetAsync(cancellationToken);

        var own = await dbContext.Set<SellerCommission>()
            .AsNoTracking()
            .Where(c => c.SellerId == sellerId)
            .Select(c => (decimal?)c.CommissionPercent)
            .FirstOrDefaultAsync(cancellationToken);

        return new EarningRates(own ?? policy.DefaultCommissionPercent, policy.TcsPercent, policy.TdsPercent);
    }
}

/// <summary>
/// The courier has paid over the cash it collected for a parcel, or staff wrote it off: the
/// seller's earnings from it can be paid on the usual terms. An earning not recorded yet is
/// covered by the delivery handler, which asks Shipping.
/// </summary>
internal sealed class CodCashReceivedEarningHandler(UPBazaarDbContext dbContext)
    : IDomainEventHandler<CodCashReceivedDomainEvent>
{
    public async Task HandleAsync(CodCashReceivedDomainEvent e, CancellationToken cancellationToken)
    {
        var waiting = await dbContext.Set<Earning>()
            .Where(x => x.OrderPartId == e.OrderPartId && x.AwaitingCash)
            .ToListAsync(cancellationToken);

        waiting.ForEach(earning => earning.CashReceived());

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
