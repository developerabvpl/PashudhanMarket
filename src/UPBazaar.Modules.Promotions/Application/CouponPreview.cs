using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Cart.Contracts;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Promotions.Contracts;
using UPBazaar.Modules.Promotions.Contracts.Dtos;
using UPBazaar.Modules.Promotions.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Promotions.Application;

/// <summary>
/// What a coupon would take off the buyer's basket as it stands, so checkout can show the
/// discount before the order is placed. Placing the order prices it again.
///
/// A free-delivery coupon is refused here when delivery is free anyway, rather than letting the
/// buyer spend a use of it on nothing.
/// </summary>
public sealed record PreviewCouponQuery(Guid BuyerId, string Code) : IQuery<CouponPreviewDto>;

internal sealed class PreviewCouponQueryValidator : AbstractValidator<PreviewCouponQuery>
{
    public PreviewCouponQueryValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(Coupon.CodeMaxLength);
}

internal sealed class PreviewCouponQueryHandler(UPBazaarDbContext dbContext, ICartService cart, ICouponPricing pricing, IDeliveryCharges delivery)
    : IQueryHandler<PreviewCouponQuery, CouponPreviewDto>
{
    public async Task<Result<CouponPreviewDto>> HandleAsync(PreviewCouponQuery query, CancellationToken cancellationToken)
    {
        var lines = await cart.GetCheckoutLinesAsync(query.BuyerId, cancellationToken);

        if (lines.IsFailure)
        {
            return Result.Failure<CouponPreviewDto>(lines.Error);
        }

        var basket = lines.Value;
        var discount = await pricing.QuoteAsync(
            query.Code,
            query.BuyerId,
            [.. basket.Select(l => new CouponLineDto(l.ProductId, l.SellerId, l.UnitPrice * l.Quantity))],
            cancellationToken);

        if (discount.IsFailure)
        {
            return Result.Failure<CouponPreviewDto>(discount.Error);
        }

        var deliveryDiscount = 0m;

        if (discount.Value.FreeDelivery)
        {
            var covered = discount.Value.Lines.Select(l => l.ProductId).ToHashSet();
            var sellers = basket.Where(l => covered.Contains(l.ProductId)).Select(l => l.SellerId).ToHashSet();
            var shares = delivery.SharesFor([.. basket.Select(l => new SellerGoodsDto(l.SellerId, l.UnitPrice * l.Quantity))]);

            deliveryDiscount = shares.Where(s => sellers.Contains(s.Key)).Sum(s => s.Value);

            if (deliveryDiscount == 0m)
            {
                return Result.Failure<CouponPreviewDto>(PromotionErrors.DeliveryAlreadyFree);
            }
        }

        var code = Coupon.Normalize(query.Code);
        var description = await dbContext.Set<Coupon>()
            .Where(c => c.Code == code)
            .Select(c => c.Description)
            .FirstAsync(cancellationToken);

        return new CouponPreviewDto(discount.Value.Code, description, discount.Value.Discount, deliveryDiscount);
    }
}
