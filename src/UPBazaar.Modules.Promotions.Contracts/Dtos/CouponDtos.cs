namespace UPBazaar.Modules.Promotions.Contracts.Dtos;

/// <summary>A coupon code and its rules.</summary>
/// <param name="Id">Public id.</param>
/// <param name="Code">What buyers type at checkout.</param>
/// <param name="Description">What it is for, in a line.</param>
/// <param name="SellerId">The seller who runs it; null for a platform coupon.</param>
/// <param name="FundedBy">Platform or Seller: who bears the discount.</param>
/// <param name="DiscountType">Percent or Flat.</param>
/// <param name="Value">The percentage, or the rupees off.</param>
/// <param name="MaxDiscount">For a percentage, the most it takes off; null for no cap.</param>
/// <param name="MinOrderValue">The least the goods it covers must come to; null for none.</param>
/// <param name="StartsAtUtc">When it can first be used.</param>
/// <param name="EndsAtUtc">When it stops working; null for no end.</param>
/// <param name="TotalLimit">How many orders may use it in all; null for no limit.</param>
/// <param name="PerBuyerLimit">How many orders each buyer may use it on.</param>
/// <param name="Uses">How many orders have used it.</param>
/// <param name="IsActive">False once ended by hand.</param>
/// <param name="SellersJoined">For a platform coupon that sellers pay for, how many have joined.</param>
/// <param name="Joined">For a seller looking at a platform campaign, whether they have joined it.</param>
public sealed record CouponDto(
    Guid Id,
    string Code,
    string Description,
    Guid? SellerId,
    string FundedBy,
    string DiscountType,
    decimal Value,
    decimal? MaxDiscount,
    decimal? MinOrderValue,
    DateTime StartsAtUtc,
    DateTime? EndsAtUtc,
    int? TotalLimit,
    int PerBuyerLimit,
    int Uses,
    bool IsActive,
    int SellersJoined,
    bool Joined);

/// <summary>What a coupon takes off the signed-in buyer's basket, for checkout to show.</summary>
/// <param name="Code">The coupon's code.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Discount">What it takes off the goods.</param>
public sealed record CouponPreviewDto(string Code, string Description, decimal Discount);
