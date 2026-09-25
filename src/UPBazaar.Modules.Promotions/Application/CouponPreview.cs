using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Cart.Contracts;
using UPBazaar.Modules.Promotions.Contracts;
using UPBazaar.Modules.Promotions.Contracts.Dtos;
using UPBazaar.Modules.Promotions.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Promotions.Application;

/// <summary>
/// What a coupon would take off the buyer's basket as it stands, so checkout can show the
/// discount before the order is placed. Placing the order prices it again.
/// </summary>
public sealed record PreviewCouponQuery(Guid BuyerId, string Code) : IQuery<CouponPreviewDto>;

internal sealed class PreviewCouponQueryValidator : AbstractValidator<PreviewCouponQuery>
{
    public PreviewCouponQueryValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(Coupon.CodeMaxLength);
}

internal sealed class PreviewCouponQueryHandler(UPBazaarDbContext dbContext, ICartService cart, ICouponPricing pricing)
    : IQueryHandler<PreviewCouponQuery, CouponPreviewDto>
{
    public async Task<Result<CouponPreviewDto>> HandleAsync(PreviewCouponQuery query, CancellationToken cancellationToken)
    {
        var lines = await cart.GetCheckoutLinesAsync(query.BuyerId, cancellationToken);

        if (lines.IsFailure)
        {
            return Result.Failure<CouponPreviewDto>(lines.Error);
        }

        var discount = await pricing.QuoteAsync(
            query.Code,
            query.BuyerId,
            [.. lines.Value.Select(l => new CouponLineDto(l.ProductId, l.SellerId, l.UnitPrice * l.Quantity))],
            cancellationToken);

        if (discount.IsFailure)
        {
            return Result.Failure<CouponPreviewDto>(discount.Error);
        }

        var code = Coupon.Normalize(query.Code);
        var description = await dbContext.Set<Coupon>()
            .Where(c => c.Code == code)
            .Select(c => c.Description)
            .FirstAsync(cancellationToken);

        return new CouponPreviewDto(discount.Value.Code, description, discount.Value.Discount);
    }
}
