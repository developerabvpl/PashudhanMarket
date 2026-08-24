using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Infrastructure.Persistence.Idempotency;

/// <summary>
/// Reserves keys in their own transaction so a concurrent duplicate loses the race on the
/// unique index rather than running the operation twice.
/// </summary>
public sealed class IdempotencyService(IServiceScopeFactory scopeFactory, IClock clock)
    : IIdempotencyService
{
    public async Task<IdempotencyLookup> TryBeginAsync(
        string key,
        string endpoint,
        string requestHash,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var existing = await db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key, cancellationToken);

        if (existing is not null)
        {
            return Evaluate(existing, endpoint, requestHash);
        }

        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = key,
            Endpoint = endpoint,
            RequestHash = requestHash,
            CreatedAtUtc = clock.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return IdempotencyLookup.Fresh();
        }
        catch (DbUpdateException)
        {
            // Lost the race: another request reserved the same key first.
            var winner = await db.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == key, cancellationToken);

            return winner is null
                ? IdempotencyLookup.Conflict()
                : Evaluate(winner, endpoint, requestHash);
        }
    }

    public async Task CompleteAsync(string key, string responsePayload, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var record = await db.IdempotencyRecords.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);

        if (record is null)
        {
            return;
        }

        record.ResponsePayload = responsePayload;
        record.CompletedAtUtc = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>SHA-256 of the request body, used to detect a key reused for different content.</summary>
    public static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static IdempotencyLookup Evaluate(IdempotencyRecord record, string endpoint, string requestHash) =>
        record.Endpoint != endpoint || record.RequestHash != requestHash
            ? IdempotencyLookup.Conflict()
            : IdempotencyLookup.Replay(record.ResponsePayload);
}
