using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Promotions.Contracts;

/// <summary>
/// Coupons as checkout needs them: what one takes off a basket, and using it for an order.
///
/// The discount comes back already spread over the lines it covers, because Orders keeps money per
/// line and per parcel - cash on delivery, refunds and seller earnings are all worked out a parcel
/// at a time.
/// </summary>
public interface ICouponPricing
{
    /// <summary>What the coupon would take off these lines for this buyer. Uses nothing up.</summary>
    Task<Result<CouponDiscountDto>> QuoteAsync(
        string code,
        Guid buyerId,
        IReadOnlyList<CouponLineDto> lines,
        CancellationToken cancellationToken);

    /// <summary>
    /// Uses the coupon for an order, checking it again as it does. Call it inside the order's own
    /// transaction, so a coupon is never used by an order that was not placed.
    /// </summary>
    Task<Result<CouponDiscountDto>> RedeemAsync(
        string code,
        Guid buyerId,
        Guid orderId,
        IReadOnlyList<CouponLineDto> lines,
        CancellationToken cancellationToken);
}

/// <summary>One line of a basket, as a coupon sees it.</summary>
/// <param name="ProductId">The product; one line per product.</param>
/// <param name="SellerId">Whose product it is.</param>
/// <param name="LineTotal">Its price times its quantity.</param>
public sealed record CouponLineDto(Guid ProductId, Guid SellerId, decimal LineTotal);

/// <summary>What a coupon takes off a basket.</summary>
/// <param name="CouponId">The coupon.</param>
/// <param name="Code">Its code, as it is stored.</param>
/// <param name="FundedBy">Platform (sellers are paid as if the buyer paid full price) or Seller (the sellers whose goods it covers bear it).</param>
/// <param name="Discount">The whole discount.</param>
/// <param name="Lines">The discount on each line it covers; lines it does not cover are left out.</param>
public sealed record CouponDiscountDto(
    Guid CouponId,
    string Code,
    string FundedBy,
    decimal Discount,
    IReadOnlyList<CouponLineDiscountDto> Lines);

/// <summary>The part of a discount that falls on one line.</summary>
public sealed record CouponLineDiscountDto(Guid ProductId, decimal Discount);
