using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Inventory.Domain;
using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Modules.Inventory.Services;

/// <summary>
/// Puts abandoned holds back on sale.
///
/// A shopper who closes the tab mid-checkout never releases anything, and without this their
/// hold would keep the stock off sale for good.
/// </summary>
public sealed partial class ReservationExpiryJob(
    UPBazaarDbContext dbContext,
    IClock clock,
    ILogger<ReservationExpiryJob> logger)
{
    /// <summary>Hangfire's id for the schedule.</summary>
    public const string RecurringJobId = "inventory.expire-reservations";

    /// <summary>Most holds expired in one run; the next run picks up any remainder.</summary>
    private const int BatchSize = 200;

    /// <summary>Expires every active hold past its time. Returns how many it expired.</summary>
    public async Task<int> ExpireAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var due = await dbContext.Set<Reservation>()
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAtUtc <= now)
            .OrderBy(r => r.ExpiresAtUtc)
            .Select(r => r.PublicId)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var service = new InventoryService(dbContext, clock);
        var expired = 0;

        foreach (var reservationId in due)
        {
            // One at a time, each in its own save: a hold that fails to expire must not keep the
            // others on hold with it.
            var result = await service.CloseAsync(reservationId, ReservationStatus.Expired, cancellationToken);

            if (result.IsSuccess)
            {
                expired++;
            }
            else
            {
                LogExpiryFailed(logger, reservationId, result.Error.Code);
            }
        }

        if (expired > 0)
        {
            LogExpired(logger, expired);
        }

        return expired;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expired {Count} stock reservation(s)")]
    private static partial void LogExpired(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not expire reservation {ReservationId}: {ErrorCode}")]
    private static partial void LogExpiryFailed(ILogger logger, Guid reservationId, string errorCode);
}
