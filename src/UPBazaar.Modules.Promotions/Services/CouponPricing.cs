using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Promotions.Contracts;
using UPBazaar.Modules.Promotions.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Promotions.Services;

/// <summary>
/// Prices and uses coupons for checkout.
///
/// Redeeming stages its changes on the shared context and saves nothing: Orders saves them with
/// the order, in one transaction, so a coupon is used exactly when an order is placed. The
/// coupon's row version stops two buyers taking its last use at once; one buyer checking out twice
/// at once is already stopped by checkout's own lock.
/// </summary>
internal sealed class CouponPricing(UPBazaarDbContext dbContext, IClock clock) : ICouponPricing
{
    public async Task<Result<CouponDiscountDto>> QuoteAsync(
        string code,
        Guid buyerId,
        IReadOnlyList<CouponLineDto> lines,
        CancellationToken cancellationToken)
    {
        var coupon = await FindAsync(code, tracked: false, cancellationToken);

        return coupon is null
            ? Result.Failure<CouponDiscountDto>(PromotionErrors.UnknownCode)
            : await QuoteAsync(coupon, buyerId, lines, cancellationToken);
    }

    public async Task<Result<CouponDiscountDto>> RedeemAsync(
        string code,
        Guid buyerId,
        Guid orderId,
        IReadOnlyList<CouponLineDto> lines,
        CancellationToken cancellationToken)
    {
        var coupon = await FindAsync(code, tracked: true, cancellationToken);

        if (coupon is null)
        {
            return Result.Failure<CouponDiscountDto>(PromotionErrors.UnknownCode);
        }

        var quote = await QuoteAsync(coupon, buyerId, lines, cancellationToken);

        if (quote.IsSuccess)
        {
            coupon.TakeUse();
            dbContext.Set<CouponRedemption>().Add(CouponRedemption.Create(coupon.Id, orderId, buyerId, quote.Value.Discount, clock.UtcNow));
        }

        return quote;
    }

    private async Task<Result<CouponDiscountDto>> QuoteAsync(
        Coupon coupon,
        Guid buyerId,
        IReadOnlyList<CouponLineDto> lines,
        CancellationToken cancellationToken)
    {
        var used = await dbContext.Set<CouponRedemption>()
            .CountAsync(r => r.CouponId == coupon.Id && r.BuyerId == buyerId && r.ReleasedAtUtc == null, cancellationToken);

        return used >= coupon.PerBuyerLimit
            ? Result.Failure<CouponDiscountDto>(PromotionErrors.AlreadyUsed)
            : coupon.Quote(lines, clock.UtcNow);
    }

    private Task<Coupon?> FindAsync(string code, bool tracked, CancellationToken cancellationToken)
    {
        var normalized = Coupon.Normalize(code);
        var coupons = dbContext.Set<Coupon>().Include(c => c.Sellers).AsQueryable();

        if (!tracked)
        {
            coupons = coupons.AsNoTracking();
        }

        return coupons.FirstOrDefaultAsync(c => c.Code == normalized, cancellationToken);
    }
}
