using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Shipping.Contracts;
using UPBazaar.Modules.Shipping.Domain;

namespace UPBazaar.Modules.Shipping.Services;

/// <summary>Answers Settlements from the parcel's receivable.</summary>
internal sealed class CodCash(UPBazaarDbContext dbContext) : ICodCash
{
    public Task<bool> IsCashInAsync(Guid orderPartId, CancellationToken cancellationToken) =>
        dbContext.Set<CodReceivable>().AnyAsync(
            r => r.OrderPartId == orderPartId
                && (r.Status == CodStatus.Received || r.Status == CodStatus.Over || r.Status == CodStatus.WrittenOff),
            cancellationToken);
}
