using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Shipping.Domain;

namespace UPBazaar.Modules.Shipping.Application;

/// <summary>
/// Matches remittance rows still waiting for their parcel, once both have been saved.
///
/// An upload and the courier's delivery update for the same AWB can land at the same moment: the
/// upload finds no receivable yet, and the delivery finds no row yet, and each saves without the
/// other. Whichever saves second runs this afterwards and sees both. Two sweeps at once are safe:
/// the receivable's row version lets one through, and the other finds the work done.
/// </summary>
internal static class CodMatching
{
    public static async Task SweepAsync(
        UPBazaarDbContext dbContext,
        IReadOnlyCollection<string> awbs,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (awbs.Count == 0)
        {
            return;
        }

        var waiting = await dbContext.Set<CodRemittanceLine>()
            .Where(l => l.ReceivableId == null && awbs.Contains(l.Awb))
            .ToListAsync(cancellationToken);

        if (waiting.Count == 0)
        {
            return;
        }

        var receivables = (await dbContext.Set<CodReceivable>()
                .Where(r => awbs.Contains(r.Awb))
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.Awb, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var line in waiting)
        {
            if (receivables.TryGetValue(line.Awb, out var receivable))
            {
                line.MatchTo(receivable, now);
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another sweep matched them first. Its changes stand; these are dropped.
            dbContext.ChangeTracker.Clear();
        }
    }
}
