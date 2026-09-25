namespace UPBazaar.Modules.Orders.Domain;

/// <summary>
/// How an order's delivery charge falls across its sellers' parcels. One rule, used both by the
/// order itself and by checkout when it shows what a free-delivery coupon would save, so the two
/// always agree to the paisa.
/// </summary>
public static class DeliveryShares
{
    /// <summary>
    /// Splits <paramref name="amount"/> in proportion to <paramref name="goods"/>, each parcel's
    /// goods: a seller shipping more of the order carries more of its delivery. Rounded down to
    /// the paisa, with whatever rounding leaves over going to the largest parcel, so the shares
    /// always add up to the charge exactly. Parcels with no goods value share it equally.
    /// </summary>
    public static decimal[] Split(decimal amount, IReadOnlyList<decimal> goods)
    {
        ArgumentNullException.ThrowIfNull(goods);

        if (goods.Count == 0)
        {
            return [];
        }

        var weight = goods.Sum();
        var shares = goods
            .Select(g => weight == 0m
                ? Math.Round(amount / goods.Count, 2, MidpointRounding.ToZero)
                : Math.Round(amount * g / weight, 2, MidpointRounding.ToZero))
            .ToArray();

        var largest = goods.Select((g, i) => (g, i)).MaxBy(x => x.g).i;
        shares[largest] += amount - shares.Sum();

        return shares;
    }
}
