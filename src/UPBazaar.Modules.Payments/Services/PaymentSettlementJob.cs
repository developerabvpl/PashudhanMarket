using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Payments.Application;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Modules.Payments.Services;

/// <summary>
/// Finishes payments that were captured but never applied to their order.
///
/// That happens when the process dies between recording the money and confirming the order, or
/// when Orders was busy at the time. Without this, such an order would be cancelled at its
/// deadline and the buyer's money kept with nothing to show for it. With it, within a minute the
/// order is either confirmed or refused - and a refusal records the whole payment as owed back.
/// </summary>
public sealed partial class PaymentSettlementJob(
    UPBazaarDbContext dbContext,
    IServiceProvider services,
    IClock clock,
    ILogger<PaymentSettlementJob> logger)
{
    /// <summary>Hangfire's id for the schedule.</summary>
    public const string RecurringJobId = "payments.settle";

    /// <summary>
    /// Left alone for this long after capture, so the job does not race the browser or the webhook
    /// that is settling the payment right now.
    /// </summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);

    private const int BatchSize = 100;

    /// <summary>Settles every stranded payment. Returns how many it moved on.</summary>
    public async Task<int> SettleAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow - Grace;

        var stranded = await dbContext.Set<Payment>()
            .AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid && p.OrderOutcome == OrderOutcome.Pending && p.PaidAtUtc <= cutoff)
            .OrderBy(p => p.PaidAtUtc)
            .Select(p => p.PublicId)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var settler = (PaymentSettler)services.GetService(typeof(PaymentSettler))!;
        var settled = 0;

        foreach (var paymentId in stranded)
        {
            var result = await settler.ApplyToOrderAsync(paymentId, cancellationToken);

            if (result.IsSuccess && result.Value.Outcome != "Processing")
            {
                settled++;
            }
            else
            {
                LogStillPending(logger, paymentId);
            }
        }

        if (settled > 0)
        {
            LogSettled(logger, settled);
        }

        return settled;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Settled {Count} stranded payment(s)")]
    private static partial void LogSettled(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment {PaymentId} is still not applied to its order")]
    private static partial void LogStillPending(ILogger logger, Guid paymentId);
}
