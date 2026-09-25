using UPBazaar.Modules.Promotions.Contracts;
using UPBazaar.Modules.Promotions.Domain;
using CouponModel = UPBazaar.Modules.Promotions.Domain.Coupon;

namespace UPBazaar.UnitTests.Promotions;

public sealed class CouponTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SellerA = Guid.NewGuid();
    private static readonly Guid SellerB = Guid.NewGuid();

    /// <summary>Seller A: 200 and 100. Seller B: 50.</summary>
    private static readonly CouponLineDto[] Basket =
    [
        new(Guid.NewGuid(), SellerA, 200m),
        new(Guid.NewGuid(), SellerA, 100m),
        new(Guid.NewGuid(), SellerB, 50m),
    ];

    [Fact]
    public void A_platform_percentage_covers_the_basket_and_is_spread_by_value()
    {
        var quote = Coupon(DiscountType.Percent, 10m).Quote(Basket, Now).Value;

        quote.Discount.ShouldBe(35m);
        quote.FundedBy.ShouldBe("Platform");
        quote.Lines.Select(l => l.Discount).ShouldBe([20m, 10m, 5m]);
    }

    [Fact]
    public void A_percentage_stops_at_its_cap()
    {
        Coupon(DiscountType.Percent, 10m, maxDiscount: 25m).Quote(Basket, Now).Value.Discount.ShouldBe(25m);
    }

    [Fact]
    public void Rounding_leftovers_land_on_the_largest_line_so_the_parts_add_up()
    {
        var quote = Coupon(DiscountType.Flat, 10m).Quote(Basket, Now).Value;

        quote.Lines.Sum(l => l.Discount).ShouldBe(10m);
        quote.Lines[0].Discount.ShouldBe(5.73m);
    }

    [Fact]
    public void No_coupon_takes_more_than_nine_tenths_of_the_goods()
    {
        Coupon(DiscountType.Flat, 1000m).Quote(Basket, Now).Value.Discount.ShouldBe(315m);
    }

    [Fact]
    public void A_seller_s_coupon_covers_only_their_goods_and_is_theirs_to_pay_for()
    {
        var coupon = CouponModel.Create("SHOP10", "Shop sale", SellerB, CouponFunding.Platform, DiscountType.Percent, 10m, null, null, Now, null, null, 1);

        var quote = coupon.Quote(Basket, Now).Value;

        coupon.FundedBy.ShouldBe(CouponFunding.Seller);
        quote.Discount.ShouldBe(5m);
        quote.Lines.ShouldHaveSingleItem().ProductId.ShouldBe(Basket[2].ProductId);
    }

    [Fact]
    public void A_campaign_covers_only_sellers_who_joined()
    {
        var campaign = CouponModel.Create("DIWALI", "Diwali", null, CouponFunding.Seller, DiscountType.Percent, 10m, null, null, Now, null, null, 1);

        campaign.Quote(Basket, Now).Error.ShouldBe(PromotionErrors.NothingCovered);

        campaign.Join(SellerA, Now).IsSuccess.ShouldBeTrue();
        campaign.Quote(Basket, Now).Value.Discount.ShouldBe(30m);

        campaign.Leave(SellerA);
        campaign.Quote(Basket, Now).Error.ShouldBe(PromotionErrors.NothingCovered);
    }

    [Fact]
    public void Only_campaigns_can_be_joined()
    {
        Coupon(DiscountType.Percent, 10m).Join(SellerA, Now).Error.ShouldBe(PromotionErrors.NotACampaign);
    }

    [Fact]
    public void The_minimum_is_of_the_goods_it_covers()
    {
        var coupon = CouponModel.Create("SHOP10", "Shop sale", SellerB, CouponFunding.Seller, DiscountType.Flat, 10m, null, 100m, Now, null, null, 1);

        coupon.Quote(Basket, Now).Error.Code.ShouldBe("promotions.coupon.below_minimum");
    }

    [Fact]
    public void A_coupon_works_only_while_it_runs_and_until_it_is_used_up()
    {
        var coupon = CouponModel.Create("LAUNCH", "Launch", null, CouponFunding.Platform, DiscountType.Flat, 10m, null, null, Now, Now.AddDays(1), 1, 1);

        coupon.Quote(Basket, Now.AddMinutes(-1)).Error.ShouldBe(PromotionErrors.NotValidNow);
        coupon.Quote(Basket, Now.AddDays(2)).Error.ShouldBe(PromotionErrors.NotValidNow);

        coupon.TakeUse();
        coupon.Quote(Basket, Now).Error.ShouldBe(PromotionErrors.UsedUp);

        coupon.GiveBackUse();
        coupon.Quote(Basket, Now).IsSuccess.ShouldBeTrue();

        coupon.End();
        coupon.Quote(Basket, Now).Error.ShouldBe(PromotionErrors.NotValidNow);
    }

    [Fact]
    public void Codes_are_compared_in_capitals()
    {
        Coupon(DiscountType.Flat, 10m).Code.ShouldBe("WELCOME");
    }

    private static CouponModel Coupon(DiscountType type, decimal value, decimal? maxDiscount = null) =>
        CouponModel.Create(" welcome ", "Welcome", null, CouponFunding.Platform, type, value, maxDiscount, null, Now, null, null, 1);
}
