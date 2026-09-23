using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.Modules.Settlements.Contracts.Dtos;
using UPBazaar.Modules.Settlements.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Settlements.Application;

/// <summary>Makes payouts now, instead of waiting for the weekly run.</summary>
public sealed record RunPayoutsCommand : ICommand<PayoutRunResultDto>;

internal sealed class RunPayoutsCommandHandler(PayoutRunner runner) : ICommandHandler<RunPayoutsCommand, PayoutRunResultDto>
{
    public async Task<Result<PayoutRunResultDto>> HandleAsync(RunPayoutsCommand command, CancellationToken cancellationToken) =>
        await runner.RunAsync(cancellationToken);
}

/// <summary>
/// Gathers every payable earning into one payout per seller.
///
/// Each seller's payout is saved on its own, so one that fails - the seller's record missing, or
/// an earning put on hold by a late return request at the same moment - leaves the others made
/// and its own earnings for the next run. Running it twice is harmless: a settled earning is not
/// payable any more.
/// </summary>
internal sealed partial class PayoutRunner(
    UPBazaarDbContext dbContext,
    ISellerDirectory sellers,
    IClock clock,
    ILogger<PayoutRunner> logger)
{
    public async Task<PayoutRunResultDto> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var sellerIds = await dbContext.Set<Earning>()
            .Where(e => e.Status == EarningStatus.Accruing && e.PayableFromUtc <= now)
            .Select(e => e.SellerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        int payouts = 0, settled = 0, skipped = 0;

        foreach (var sellerId in sellerIds)
        {
            var account = await sellers.GetPayoutAccountAsync(sellerId, cancellationToken);

            if (account is null)
            {
                LogNoSeller(logger, sellerId);
                skipped++;

                continue;
            }

            var earnings = await dbContext.Set<Earning>()
                .Where(e => e.SellerId == sellerId && e.Status == EarningStatus.Accruing && e.PayableFromUtc <= now)
                .ToListAsync(cancellationToken);

            var payout = Payout.Create(sellerId, account.ShopName, account.AccountHolder, account.AccountNumber, account.Ifsc, earnings);
            dbContext.Set<Payout>().Add(payout);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                payouts++;
                settled += earnings.Count;
            }
            catch (DbUpdateConcurrencyException)
            {
                LogClash(logger, sellerId);
                dbContext.ChangeTracker.Clear();
                skipped++;
            }
        }

        return new PayoutRunResultDto(payouts, settled, skipped);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Seller {SellerId} is owed money but has no seller record to pay to")]
    private static partial void LogNoSeller(ILogger logger, Guid sellerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payout for seller {SellerId} left for the next run: an earning changed while it was being made")]
    private static partial void LogClash(ILogger logger, Guid sellerId);
}

/// <summary>The weekly payout run, scheduled with Hangfire.</summary>
public sealed class PayoutRunJob(IDispatcher dispatcher)
{
    /// <summary>Hangfire's id for the schedule.</summary>
    public const string RecurringJobId = "settlements.payouts";

    /// <summary>Mondays 01:00 UTC, which is 06:30 in India: payouts are ready when finance start the week.</summary>
    public const string Schedule = "0 1 * * 1";

    public async Task RunAsync(CancellationToken cancellationToken) =>
        await dispatcher.SendAsync(new RunPayoutsCommand(), cancellationToken);
}
