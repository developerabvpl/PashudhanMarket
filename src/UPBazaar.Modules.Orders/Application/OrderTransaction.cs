using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Application;

/// <summary>
/// Runs an order change and its stock movement as one database transaction.
///
/// Needed because Inventory saves its own work: reserving, committing, releasing and returning
/// stock each call SaveChanges on the shared context. Without a transaction around them, a stock
/// movement could land while the order change beside it failed, leaving stock held for an order
/// that does not exist, or committed for one that was cancelled. Inside one, either both happen
/// or neither does.
///
/// Runs through the execution strategy because the context retries transient SQL failures, and
/// a retrying context refuses a transaction it cannot replay. On a retry the whole piece of work
/// runs again from a cleared tracker, so it must read everything it needs inside the work
/// delegate itself.
/// </summary>
internal sealed class OrderTransaction(UPBazaarDbContext dbContext)
{
    public async Task<Result<T>> RunAsync<T>(
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            Result<T> result;

            try
            {
                result = await work(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another request changed the same order, cart or stock first. Nothing of this
                // attempt survives the rollback, so the caller retrying is safe.
                result = Result.Failure<T>(OrderErrors.ConcurrentChange);
            }

            if (result.IsSuccess)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
            }

            return result;
        });
    }

    /// <summary>
    /// True when Inventory's own retry cleared the tracker under an entity loaded before it ran.
    /// Changes made to that instance would then silently never be saved, so the caller gives up
    /// and reports a conflict instead.
    /// </summary>
    public bool WasDetached(object entity) => dbContext.Entry(entity).State == EntityState.Detached;
}
