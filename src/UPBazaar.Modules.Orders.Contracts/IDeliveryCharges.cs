namespace UPBazaar.Modules.Orders.Contracts;

/// <summary>
/// The delivery charge checkout would put on a basket, and how it would fall across the sellers'
/// parcels - for showing a buyer what a free-delivery coupon saves before the order is placed.
/// </summary>
public interface IDeliveryCharges
{
    /// <summary>
    /// Each seller's share of the delivery charge on these goods, keyed by seller. All zero when
    /// the goods reach the free-delivery value anyway.
    /// </summary>
    IReadOnlyDictionary<Guid, decimal> SharesFor(IReadOnlyList<SellerGoodsDto> goods);
}

/// <summary>Goods in a basket and whose they are; one per line is fine.</summary>
/// <param name="SellerId">The seller.</param>
/// <param name="Amount">What the goods come to, before any coupon.</param>
public sealed record SellerGoodsDto(Guid SellerId, decimal Amount);
