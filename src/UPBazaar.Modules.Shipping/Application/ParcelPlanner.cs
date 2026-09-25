using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Contracts;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Domain;

namespace UPBazaar.Modules.Shipping.Application;

/// <summary>
/// Works out a parcel from the products in a part, and where it is collected from.
///
/// The parcel is the units stacked in one box: weights add up, the box is as long and wide as
/// the largest unit and as tall as all of them together. That overstates a box that was packed
/// cleverly, which is the safe side - couriers surcharge a parcel that turns out bigger than
/// booked, and never refund one that turns out smaller.
/// </summary>
internal sealed class ParcelPlanner(UPBazaarDbContext dbContext, IProductCatalog catalog)
{
    public async Task<(ParcelDto? Parcel, IReadOnlyList<string> MissingSkus)> PlanAsync(
        ShippablePartDto part,
        CancellationToken cancellationToken)
    {
        var packages = await catalog.GetPackagesAsync([.. part.Lines.Select(l => l.ProductId)], cancellationToken);
        var missing = part.Lines.Where(l => !packages.ContainsKey(l.ProductId)).Select(l => l.Sku).ToList();

        if (missing.Count > 0)
        {
            return (null, missing);
        }

        var units = part.Lines.Select(l => (Package: packages[l.ProductId], l.Quantity)).ToList();

        return (new ParcelDto(
            units.Sum(u => u.Package.WeightGrams * u.Quantity),
            units.Max(u => u.Package.LengthCm),
            units.Max(u => u.Package.BreadthCm),
            units.Sum(u => u.Package.HeightCm * u.Quantity)), []);
    }

    /// <summary>The name of the seller's own pickup location, else the platform warehouse's, else none.</summary>
    public async Task<string?> PickupLocationForAsync(Guid sellerId, CancellationToken cancellationToken) =>
        (await PickupFromAsync(sellerId, cancellationToken))?.Name;

    /// <summary>The PIN code of the same place, for pricing the parcel; null when none was recorded.</summary>
    public async Task<string?> PickupPincodeForAsync(Guid sellerId, CancellationToken cancellationToken) =>
        (await PickupFromAsync(sellerId, cancellationToken))?.Pincode;

    private async Task<PickupLocation?> PickupFromAsync(Guid sellerId, CancellationToken cancellationToken)
    {
        var locations = await dbContext.Set<PickupLocation>()
            .AsNoTracking()
            .Where(l => l.SellerId == sellerId || l.SellerId == null)
            .ToListAsync(cancellationToken);

        return locations.FirstOrDefault(l => l.SellerId == sellerId) ?? locations.FirstOrDefault();
    }
}
