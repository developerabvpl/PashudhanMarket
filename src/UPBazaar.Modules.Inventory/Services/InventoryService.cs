using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Inventory.Contracts;
using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.Modules.Inventory.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Inventory.Services;

/// <summary>
/// Implements <see cref="IInventoryService"/>.
///
/// Reserve, release and commit save their own work and retry on a concurrency conflict. A
/// conflict means another checkout touched the same stock row in the moment between reading and
/// saving; re-reading and re-checking is always correct, and for release and commit it is also
/// necessary, because those must not fail just because the shop is busy.
/// </summary>
internal sealed class InventoryService(UPBazaarDbContext dbContext, IClock clock) : IInventoryService
{
    private const int MaxAttempts = 3;

    public async Task<IReadOnlyDictionary<Guid, StockLevelDto>> GetStockLevelsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        var found = await dbContext.Set<StockItem>()
            .AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, s => s.ToDto(), cancellationToken);

        foreach (var productId in productIds)
        {
            found.TryAdd(productId, StockLevelDto.None(productId));
        }

        return found;
    }

    public async Task<Result> StageOpeningStockAsync(
        Guid productId,
        int quantity,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);

        if (await dbContext.Set<StockItem>().AnyAsync(s => s.ProductId == productId, cancellationToken))
        {
            return Result.Failure(Error.Conflict(
                "inventory.stock.already_opened",
                "This product already has stock recorded. Use a receipt or a count instead."));
        }

        var item = StockItem.Create(productId);
        dbContext.Set<StockItem>().Add(item);

        if (quantity == 0)
        {
            return Result.Success();
        }

        var result = item.Receive(quantity, "Opening stock", reference: null, clock.UtcNow, actor: null);

        // A product that did not exist a moment ago cannot have had anyone waiting for it to come
        // back into stock, so "replenished" would be noise.
        item.ClearDomainEvents();

        return result;
    }

    public async Task<Result<Guid>> ReserveAsync(
        string reference,
        IReadOnlyList<ReservationLineDto> lines,
        TimeSpan holdFor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0 || lines.Any(l => l.Quantity <= 0) || holdFor <= TimeSpan.Zero)
        {
            return Result.Failure<Guid>(Error.Validation(
                "inventory.reservation.invalid",
                "A reservation needs at least one line, positive quantities and a positive hold time."));
        }

        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
        {
            return Result.Failure<Guid>(InventoryErrors.DuplicateLine);
        }

        return await WithRetryAsync(async () =>
        {
            var items = await LoadAsync(lines.Select(l => l.ProductId), cancellationToken);

            // Checked in full before anything changes, so a short line cannot leave the lines
            // before it half-reserved in memory.
            if (lines.Any(l => !items.TryGetValue(l.ProductId, out var item) || item.AvailableQuantity < l.Quantity))
            {
                return Result.Failure<Guid>(InventoryErrors.InsufficientStock);
            }

            var now = clock.UtcNow;
            var reservation = Reservation.Create(
                reference, lines.Select(l => (l.ProductId, l.Quantity)), now, holdFor);

            foreach (var line in lines)
            {
                items[line.ProductId].Reserve(line.Quantity, reference, now);
            }

            dbContext.Set<Reservation>().Add(reservation);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success(reservation.PublicId);
        });
    }

    public async Task<Result> ReleaseAsync(Guid reservationId, CancellationToken cancellationToken) =>
        await CloseAsync(reservationId, ReservationStatus.Released, cancellationToken);

    public async Task<Result> CommitAsync(Guid reservationId, CancellationToken cancellationToken) =>
        await CloseAsync(reservationId, ReservationStatus.Committed, cancellationToken);

    /// <summary>
    /// Ends a hold. Released and Expired put the stock back on sale; Committed removes it.
    /// Ending an already-ended hold is a no-op, except that committing one that ended any other
    /// way is refused: that stock may already belong to somebody else.
    /// </summary>
    internal async Task<Result> CloseAsync(
        Guid reservationId,
        ReservationStatus outcome,
        CancellationToken cancellationToken)
    {
        var result = await WithRetryAsync(async () =>
        {
            var reservation = await dbContext.Set<Reservation>()
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.PublicId == reservationId, cancellationToken);

            if (reservation is null)
            {
                return Result.Failure<bool>(InventoryErrors.ReservationNotFound);
            }

            if (reservation.Status != ReservationStatus.Active)
            {
                return reservation.Status == outcome || outcome != ReservationStatus.Committed
                    ? Result.Success(true)
                    : Result.Failure<bool>(InventoryErrors.ReservationNotActive);
            }

            var now = clock.UtcNow;

            // Past its time but not yet swept up by the expiry job: as far as a commit is
            // concerned, it has already expired. Otherwise a slow payment could complete against a hold the shopper was told
            // had lapsed. The job releases the stock on its next run.
            if (outcome == ReservationStatus.Committed && reservation.ExpiresAtUtc <= now)
            {
                return Result.Failure<bool>(InventoryErrors.ReservationNotActive);
            }

            var items = await LoadAsync(reservation.Lines.Select(l => l.ProductId), cancellationToken);

            foreach (var line in reservation.Lines)
            {
                var item = items[line.ProductId];

                var moved = outcome == ReservationStatus.Committed
                    ? item.Commit(line.Quantity, reservation.Reference, now)
                    : item.Release(line.Quantity, reservation.Reference, now);

                if (moved.IsFailure)
                {
                    return Result.Failure<bool>(moved.Error);
                }
            }

            reservation.Close(outcome, now);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success(true);
        });

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    private Task<Dictionary<Guid, StockItem>> LoadAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var ids = productIds.ToList();

        return dbContext.Set<StockItem>()
            .Where(s => ids.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, cancellationToken);
    }

    /// <summary>
    /// Runs the operation again from a fresh read when another request saved the same stock rows
    /// first. The tracker is cleared between attempts so the retry sees the database, not the
    /// losing attempt's in-memory changes.
    /// </summary>
    private async Task<Result<T>> WithRetryAsync<T>(Func<Task<Result<T>>> operation)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await operation();

                if (result.IsFailure)
                {
                    dbContext.ChangeTracker.Clear();
                }

                return result;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();

                return Result.Failure<T>(InventoryErrors.ConcurrentChange);
            }
        }
    }
}

/// <summary>Maps inventory entities to the DTOs the API and other modules see.</summary>
internal static class InventoryMappings
{
    public static StockLevelDto ToDto(this StockItem item) => new(
        item.ProductId,
        item.OnHandQuantity,
        item.ReservedQuantity,
        item.AvailableQuantity);

    public static StockMovementDto ToDto(this StockMovement movement) => new(
        movement.PublicId,
        movement.Type.ToString(),
        movement.OnHandChange,
        movement.ReservedChange,
        movement.OnHandAfter,
        movement.Reason,
        movement.Reference,
        movement.OccurredAtUtc,
        movement.RecordedBy);
}
