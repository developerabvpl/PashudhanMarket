using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Promotions.Domain;

/// <summary>
/// An order that used a coupon: what it took off, and for whom. What counts a buyer's uses against
/// the coupon's per-buyer limit; an order cancelled outright gives its use back.
/// </summary>
public sealed class CouponRedemption : Entity
{
    private CouponRedemption()
    {
    }

    public long CouponId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid BuyerId { get; private set; }

    public decimal Discount { get; private set; }

    public DateTime RedeemedAtUtc { get; private set; }

    /// <summary>When the order was cancelled and the use given back; null while it stands.</summary>
    public DateTime? ReleasedAtUtc { get; private set; }

    public static CouponRedemption Create(long couponId, Guid orderId, Guid buyerId, decimal discount, DateTime now) => new()
    {
        CouponId = couponId,
        OrderId = orderId,
        BuyerId = buyerId,
        Discount = discount,
        RedeemedAtUtc = now,
    };

    public void Release(DateTime now) => ReleasedAtUtc ??= now;
}
