using System.Globalization;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Promotions.Domain;

/// <summary>Every failure this module can return. The coupon messages are what a buyer reads at checkout.</summary>
public static class PromotionErrors
{
    public static readonly Error UnknownCode = Error.Validation(
        "promotions.coupon.unknown",
        "That coupon code is not recognised.");

    public static readonly Error NotValidNow = Error.Validation(
        "promotions.coupon.not_valid_now",
        "That coupon is not valid now.");

    public static readonly Error UsedUp = Error.Validation(
        "promotions.coupon.used_up",
        "That coupon has been used as many times as it can be.");

    public static readonly Error AlreadyUsed = Error.Validation(
        "promotions.coupon.already_used",
        "You have already used this coupon as many times as it allows.");

    public static readonly Error NothingCovered = Error.Validation(
        "promotions.coupon.nothing_covered",
        "That coupon does not cover anything in your basket.");

    public static readonly Error DeliveryAlreadyFree = Error.Validation(
        "promotions.coupon.delivery_already_free",
        "Delivery is already free on this order, so that coupon takes nothing off.");

    public static Error BelowMinimum(decimal minimum) => Error.Validation(
        "promotions.coupon.below_minimum",
        string.Create(CultureInfo.InvariantCulture, $"That coupon needs at least Rs {minimum:0.##} of the goods it covers."));

    public static readonly Error CodeTaken = Error.Conflict(
        "promotions.coupon.code_taken",
        "There is already a coupon with that code.");

    public static readonly Error CouponNotFound = Error.NotFound(
        "promotions.coupon.not_found",
        "Coupon not found.");

    public static readonly Error NotACampaign = Error.Conflict(
        "promotions.coupon.not_a_campaign",
        "Only platform coupons that sellers pay for can be joined.");

    public static readonly Error ConcurrentChange = Error.Conflict(
        "promotions.concurrent_change",
        "The coupon was used by someone else at the same moment. Try again.");
}
