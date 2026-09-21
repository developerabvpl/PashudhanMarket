using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Application;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Orders.Services;

/// <summary>
/// Cancels online orders whose payment never arrived, and releases their stock.
///
/// A buyer who closes the payment page leaves an order holding stock that nobody can buy. This
/// runs a few minutes ahead of Inventory's own expiry of the same hold, so the order is cancelled
/// with a reason the buyer can read rather than left pending over stock that has gone.
/// </summary>
public sealed partial class UnpaidOrderExpiryJob(
    UPBazaarDbContext dbContext,
    IDispatcher dispatcher,
    IClock clock,
    ILogger<UnpaidOrderExpiryJob> logger)
{
    /// <summary>Hangfire's id for the schedule.</summary>
    public const string RecurringJobId = "orders.cancel-unpaid";

    /// <summary>The reason recorded on the order and shown to the buyer.</summary>
    public const string Reason = "Payment was not received in time.";

    /// <summary>Most orders cancelled in one run; the next run picks up any remainder.</summary>
    private const int BatchSize = 100;

    /// <summary>Cancels every unpaid online order past its deadline. Returns how many it cancelled.</summary>
    public async Task<int> CancelUnpaidAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var due = await dbContext.Set<Order>()
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.PendingPayment && o.PaymentDueAtUtc <= now)
            .OrderBy(o => o.PaymentDueAtUtc)
            .Select(o => o.PublicId)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var cancelled = 0;

        foreach (var orderId in due)
        {
            // One at a time, each in its own transaction: an order that fails to cancel must not
            // hold the others back. A payment that lands mid-run wins the row version and the
            // cancel simply fails, which is the right outcome.
            var result = await dispatcher.SendAsync(new CancelOrderCommand(orderId, BuyerId: null, Reason), cancellationToken);

            if (result.IsSuccess)
            {
                cancelled++;
            }
            else
            {
                LogCancelFailed(logger, orderId, result.Error.Code);
            }
        }

        if (cancelled > 0)
        {
            LogCancelled(logger, cancelled);
        }

        return cancelled;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancelled {Count} unpaid order(s)")]
    private static partial void LogCancelled(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not cancel unpaid order {OrderId}: {ErrorCode}")]
    private static partial void LogCancelFailed(ILogger logger, Guid orderId, string errorCode);
}
