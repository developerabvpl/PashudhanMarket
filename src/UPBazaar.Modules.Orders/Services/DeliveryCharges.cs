using Microsoft.Extensions.Options;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Orders.Domain;

namespace UPBazaar.Modules.Orders.Services;

/// <summary>
/// Works out delivery shares exactly as placing the order would: the charge judged on all the
/// goods, split over the sellers in the order they first appear.
/// </summary>
internal sealed class DeliveryCharges(IOptions<OrdersModuleOptions> options) : IDeliveryCharges
{
    public IReadOnlyDictionary<Guid, decimal> SharesFor(IReadOnlyList<SellerGoodsDto> goods)
    {
        ArgumentNullException.ThrowIfNull(goods);

        var sellers = goods.GroupBy(g => g.SellerId).Select(g => (Seller: g.Key, Amount: g.Sum(x => x.Amount))).ToList();
        var shares = DeliveryShares.Split(options.Value.DeliveryFeeFor(goods.Sum(g => g.Amount)), [.. sellers.Select(s => s.Amount)]);

        return sellers.Select((s, i) => (s.Seller, Share: shares[i])).ToDictionary(x => x.Seller, x => x.Share);
    }
}
