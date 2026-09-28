using UPBazaar.Modules.Promotions.Contracts;
using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Promotions.Domain;

/// <summary>Who bears a coupon's discount. Stored by name.</summary>
public enum CouponFunding
{
    /// <summary>The platform: sellers are paid as if the buyer had paid full price.</summary>
    Platform = 0,

    /// <summary>The sellers whose goods it covers: they are paid on the discounted price.</summary>
    Seller = 1,
}

/// <summary>How a coupon's discount is worked out. Stored by name.</summary>
public enum DiscountType
{
    /// <summary>A percentage of the goods it covers, optionally capped.</summary>
    Percent = 0,

    /// <summary>A fixed amount off, never more than the goods it covers.</summary>
    Flat = 1,

    /// <summary>
    /// No delivery charge on the parcels of the goods it covers. Takes nothing off the goods, so it
    /// has no value.
    /// </summary>
    FreeDelivery = 2,
}

/// <summary>
/// A code a buyer types at checkout for money off.
///
/// Who runs it decides what it covers. A seller's own coupon covers their products only, and they
/// pay for it. A platform coupon covers the whole basket when the platform pays; when sellers pay,
/// it covers only the products of sellers who chose to join, because nobody pays for a discount
/// they did not agree to.
///
/// Coupons are not edited once made - a buyer who read "10% off" should not find it has become 5% -
/// only ended. A new offer is a new code.
/// </summary>
public sealed class Coupon : AggregateRoot, IAuditable
{
    public const int CodeMaxLength = 20;

    public const int DescriptionMaxLength = 120;

    /// <summary>The most of the goods it covers that any coupon takes off.</summary>
    public const decimal MaxShare = 0.9m;

    private readonly List<CouponSeller> _sellers = [];

    private Coupon()
    {
    }

    /// <summary>Upper case, as stored and compared.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>The seller who runs it; null for the platform's.</summary>
    public Guid? SellerId { get; private set; }

    public CouponFunding FundedBy { get; private set; }

    public DiscountType DiscountType { get; private set; }

    public decimal Value { get; private set; }

    public decimal? MaxDiscount { get; private set; }

    public decimal? MinOrderValue { get; private set; }

    public DateTime StartsAtUtc { get; private set; }

    public DateTime? EndsAtUtc { get; private set; }

    public int? TotalLimit { get; private set; }

    public int PerBuyerLimit { get; private set; }

    /// <summary>Orders using it now: taken on redemption, given back if the order is cancelled.</summary>
    public int Uses { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>For a platform coupon that sellers pay for, the sellers who joined.</summary>
    public IReadOnlyCollection<CouponSeller> Sellers => _sellers.AsReadOnly();

    /// <summary>Optimistic concurrency: two buyers taking the last use at once must not both get it.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>A platform coupon that only covers the sellers who joined it.</summary>
    public bool IsCampaign => SellerId is null && FundedBy == CouponFunding.Seller;

    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    public static Coupon Create(
        string code,
        string description,
        Guid? sellerId,
        CouponFunding fundedBy,
        DiscountType discountType,
        decimal value,
        decimal? maxDiscount,
        decimal? minOrderValue,
        DateTime startsAtUtc,
        DateTime? endsAtUtc,
        int? totalLimit,
        int perBuyerLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return new Coupon
        {
            Code = Normalize(code),
            Description = description.Trim(),
            SellerId = sellerId,

            // A seller's own coupon is always theirs to pay for.
            FundedBy = sellerId is null ? fundedBy : CouponFunding.Seller,
            DiscountType = discountType,
            Value = discountType == DiscountType.FreeDelivery ? 0m : value,
            MaxDiscount = discountType == DiscountType.Percent ? maxDiscount : null,
            MinOrderValue = minOrderValue,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc,
            TotalLimit = totalLimit,
            PerBuyerLimit = perBuyerLimit,
            IsActive = true,
        };
    }

    public void End() => IsActive = false;

    /// <summary>A seller joins a platform campaign, agreeing to pay its discount on their goods.</summary>
    public Result Join(Guid sellerId, DateTime now)
    {
        if (!IsCampaign)
        {
            return Result.Failure(PromotionErrors.NotACampaign);
        }

        if (_sellers.All(s => s.SellerId != sellerId))
        {
            _sellers.Add(CouponSeller.Create(sellerId, now));
        }

        return Result.Success();
    }

    /// <summary>A seller leaves a campaign: orders from now on get no discount on their goods.</summary>
    public Result Leave(Guid sellerId)
    {
        if (!IsCampaign)
        {
            return Result.Failure(PromotionErrors.NotACampaign);
        }

        _sellers.RemoveAll(s => s.SellerId == sellerId);

        return Result.Success();
    }

    /// <summary>
    /// What the coupon takes off these lines, spread over the lines it covers. Checks everything
    /// but the buyer's own uses, which are counted where the redemptions are.
    /// </summary>
    public Result<CouponDiscountDto> Quote(IReadOnlyList<CouponLineDto> lines, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (!IsActive || now < StartsAtUtc || (EndsAtUtc is { } ends && now > ends))
        {
            return Result.Failure<CouponDiscountDto>(PromotionErrors.NotValidNow);
        }

        if (TotalLimit is { } limit && Uses >= limit)
        {
            return Result.Failure<CouponDiscountDto>(PromotionErrors.UsedUp);
        }

        var covered = lines.Where(Covers).ToList();

        if (covered.Count == 0)
        {
            return Result.Failure<CouponDiscountDto>(PromotionErrors.NothingCovered);
        }

        var goods = covered.Sum(l => l.LineTotal);

        if (MinOrderValue is { } minimum && goods < minimum)
        {
            return Result.Failure<CouponDiscountDto>(PromotionErrors.BelowMinimum(minimum));
        }

        if (DiscountType == DiscountType.FreeDelivery)
        {
            // The goods keep their price; the lines say whose parcels travel free.
            return new CouponDiscountDto(
                PublicId, Code, FundedBy.ToString(), 0m, [.. covered.Select(l => new CouponLineDiscountDto(l.ProductId, 0m))], FreeDelivery: true, MinOrderValue);
        }

        var discount = DiscountType == DiscountType.Percent
            ? Math.Min(Paise(goods * Value / 100m), MaxDiscount ?? decimal.MaxValue)
            : Value;

        // Never more than nine-tenths of the goods: there is always something to pay, and an
        // online payment of nothing is not one a gateway will take.
        discount = Math.Min(discount, Paise(goods * MaxShare));

        return new CouponDiscountDto(PublicId, Code, FundedBy.ToString(), discount, Spread(discount, covered), MinOrderValue: MinOrderValue);
    }

    public void TakeUse() => Uses++;

    public void GiveBackUse() => Uses = Math.Max(0, Uses - 1);

    private bool Covers(CouponLineDto line) => SellerId switch
    {
        { } owner => line.SellerId == owner,
        null when FundedBy == CouponFunding.Platform => true,
        _ => _sellers.Any(s => s.SellerId == line.SellerId),
    };

    /// <summary>
    /// Spreads the discount over the lines in proportion to their value, rounded down to the paisa,
    /// with what rounding leaves over on the largest line, so the parts add up to the whole.
    /// </summary>
    private static List<CouponLineDiscountDto> Spread(decimal discount, List<CouponLineDto> lines)
    {
        var goods = lines.Sum(l => l.LineTotal);
        var shares = lines.Select(l => Math.Round(discount * l.LineTotal / goods, 2, MidpointRounding.ToZero)).ToArray();
        var largest = lines.Select((l, i) => (l.LineTotal, i)).MaxBy(x => x.LineTotal).i;
        shares[largest] += discount - shares.Sum();

        return [.. lines.Select((l, i) => new CouponLineDiscountDto(l.ProductId, shares[i]))];
    }

    private static decimal Paise(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

/// <summary>A seller who joined a platform campaign.</summary>
public sealed class CouponSeller : Entity
{
    private CouponSeller()
    {
    }

    public long CouponId { get; private set; }

    public Guid SellerId { get; private set; }

    public DateTime JoinedAtUtc { get; private set; }

    internal static CouponSeller Create(Guid sellerId, DateTime now) => new() { SellerId = sellerId, JoinedAtUtc = now };
}
