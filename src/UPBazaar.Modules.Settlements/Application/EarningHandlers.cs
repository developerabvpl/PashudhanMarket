using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Settlements.Domain;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Settlements.Application;

/// <summary>
/// A parcel was delivered: the seller has earned from it, at today's rates, payable once the
/// buyer's return window closes. The outbox may deliver the event twice; the second finds the
/// earning already there.
/// </summary>
internal sealed class OrderPartDeliveredEarningHandler(UPBazaarDbContext dbContext, PolicyReader policy)
    : IDomainEventHandler<OrderPartDeliveredDomainEvent>
{
    public async Task HandleAsync(OrderPartDeliveredDomainEvent e, CancellationToken cancellationToken)
    {
        if (await dbContext.Set<Earning>().AnyAsync(x => x.OrderPartId == e.PartId, cancellationToken))
        {
            return;
        }

        var rates = await policy.RatesForAsync(e.SellerId, cancellationToken);

        dbContext.Set<Earning>().Add(Earning.Create(
            e.SellerId, e.OrderId, e.Number, e.PartId, e.Subtotal, e.Currency, e.DeliveredAtUtc, e.ReturnWindowClosesAtUtc, rates));

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
/// Applies a change to a parcel's earning. A parcel with no earning - delivered before Settlements
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
        var earning = await dbContext.Set<Earning>().FirstOrDefaultAsync(x => x.OrderPartId == partId, cancellationToken);

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
