using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Orders.Contracts.Permissions;
using UPBazaar.Modules.Promotions.Application;
using UPBazaar.Modules.Promotions.Contracts.Dtos;
using UPBazaar.Modules.Promotions.Contracts.Permissions;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Promotions.Api;

/// <summary>A buyer checking what a coupon takes off their basket.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/promotions/coupons")]
[Produces("application/json")]
public sealed class CouponsController(IDispatcher dispatcher, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Previews a coupon against the caller's basket.</summary>
    [HttpPost("preview")]
    [Authorize(OrdersPermissions.OwnWrite)]
    [EndpointSummary("Preview a coupon")]
    [EndpointDescription(
        "What the coupon would take off the basket as it stands. The order is priced again when it "
        + "is placed, so a basket or coupon that changes in between is caught then.")]
    [ProducesResponseType<CouponPreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CouponPreviewDto>> Preview(CouponCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Guid.TryParse(currentUser.UserId, out var buyer)
            ? (await dispatcher.QueryAsync(new PreviewCouponQuery(buyer, request.Code), cancellationToken)).ToActionResult()
            : Unauthorized();
    }
}

/// <summary>The platform's coupons, for marketing staff.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/promotions/coupons")]
[Produces("application/json")]
public sealed class AdminCouponsController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Lists every coupon.</summary>
    [HttpGet]
    [Authorize(PromotionsPermissions.CampaignsRead)]
    [EndpointSummary("List coupons")]
    [EndpointDescription("The platform's and sellers' coupons, newest first.")]
    [ProducesResponseType<IReadOnlyList<CouponDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CouponDto>>> List(CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new ListCouponsQuery(SellerId: null), cancellationToken)).ToActionResult();

    /// <summary>Creates a platform coupon.</summary>
    [HttpPost]
    [Authorize(PromotionsPermissions.CampaignsWrite)]
    [EndpointSummary("Create a platform coupon")]
    [EndpointDescription(
        "FundedBy Platform covers the whole basket and sellers are paid in full. FundedBy Seller makes "
        + "it a campaign: it covers only the goods of sellers who join it, and they bear the discount.")]
    [ProducesResponseType<CouponDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CouponDto>> Create(CreateCouponRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(request.ToCommand(sellerId: null, request.FundedBy), cancellationToken)).ToActionResult();
    }

    /// <summary>Ends a coupon.</summary>
    [HttpPost("{couponId:guid}/end")]
    [Authorize(PromotionsPermissions.CampaignsWrite)]
    [EndpointSummary("End a coupon")]
    [EndpointDescription("It stops working at once. Orders that used it keep their discount.")]
    [ProducesResponseType<CouponDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CouponDto>> End(Guid couponId, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new EndCouponCommand(couponId, SellerId: null), cancellationToken)).ToActionResult();
}

/// <summary>A seller's own coupons, and the platform campaigns they can join. The seller is found from the token.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/seller/promotions")]
[Produces("application/json")]
[Authorize(PromotionsPermissions.OwnWrite)]
public sealed class SellerCouponsController(IDispatcher dispatcher, ICurrentUser currentUser, ISellerDirectory sellers)
    : ControllerBase
{
    /// <summary>Lists the seller's own coupons.</summary>
    [HttpGet("coupons")]
    [EndpointSummary("My coupons")]
    [ProducesResponseType<IReadOnlyList<CouponDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<IReadOnlyList<CouponDto>>> List(CancellationToken cancellationToken) =>
        AsSeller(seller => dispatcher.QueryAsync(new ListCouponsQuery(seller), cancellationToken));

    /// <summary>Creates a coupon on the seller's products.</summary>
    [HttpPost("coupons")]
    [EndpointSummary("Create a coupon")]
    [EndpointDescription("Covers only your products, and you bear the discount: you are paid on the discounted price.")]
    [ProducesResponseType<CouponDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<CouponDto>> Create(CreateCouponRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller(seller => dispatcher.SendAsync(request.ToCommand(seller, "Seller"), cancellationToken));
    }

    /// <summary>Ends one of the seller's coupons.</summary>
    [HttpPost("coupons/{couponId:guid}/end")]
    [EndpointSummary("End my coupon")]
    [ProducesResponseType<CouponDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<CouponDto>> End(Guid couponId, CancellationToken cancellationToken) =>
        AsSeller(seller => dispatcher.SendAsync(new EndCouponCommand(couponId, seller), cancellationToken));

    /// <summary>Lists platform campaigns the seller can join.</summary>
    [HttpGet("campaigns")]
    [EndpointSummary("Platform campaigns")]
    [EndpointDescription("Running platform coupons that sellers pay for, and whether you have joined each.")]
    [ProducesResponseType<IReadOnlyList<CouponDto>>(StatusCodes.Status200OK)]
    public Task<ActionResult<IReadOnlyList<CouponDto>>> Campaigns(CancellationToken cancellationToken) =>
        AsSeller(seller => dispatcher.QueryAsync(new ListCampaignsQuery(seller), cancellationToken));

    /// <summary>Joins or leaves a platform campaign.</summary>
    [HttpPut("campaigns/{couponId:guid}/joined")]
    [EndpointSummary("Join or leave a campaign")]
    [EndpointDescription("While you are in, the campaign's discount applies to your products, and you bear it.")]
    [ProducesResponseType<CouponDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<CouponDto>> SetJoined(Guid couponId, CampaignJoinedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller(seller => dispatcher.SendAsync(new SetCampaignJoinedCommand(couponId, seller, request.Joined), cancellationToken));
    }

    private async Task<ActionResult<T>> AsSeller<T>(Func<Guid, Task<Result<T>>> action)
    {
        var seller = Guid.TryParse(currentUser.UserId, out var user)
            ? await sellers.GetApprovedSellerIdAsync(user, HttpContext.RequestAborted)
            : null;

        return seller is { } id
            ? (await action(id)).ToActionResult()
            : Result.Failure<T>(SellerAccessErrors.NotAnApprovedSeller).ToActionResult();
    }
}

/// <param name="Code">The coupon code, as the buyer typed it.</param>
public sealed record CouponCodeRequest(string Code);

/// <param name="Joined">True to join, false to leave.</param>
public sealed record CampaignJoinedRequest(bool Joined);

/// <param name="Code">What buyers type: 4 to 20 letters, digits or hyphens.</param>
/// <param name="Description">What it is for, in a line.</param>
/// <param name="DiscountType">Percent, Flat or FreeDelivery.</param>
/// <param name="Value">The percentage (up to 90), or the rupees off; ignored for free delivery.</param>
/// <param name="FundedBy">Platform or Seller; for staff only - a seller's coupon is always theirs to pay for.</param>
/// <param name="MaxDiscount">For a percentage, the most it takes off.</param>
/// <param name="MinOrderValue">The least the goods it covers must come to.</param>
/// <param name="StartsAtUtc">When it can first be used; now if left out.</param>
/// <param name="EndsAtUtc">When it stops working; never if left out.</param>
/// <param name="TotalLimit">How many orders may use it in all; no limit if left out.</param>
/// <param name="PerBuyerLimit">How many orders each buyer may use it on; 1 if left out.</param>
public sealed record CreateCouponRequest(
    string Code,
    string Description,
    string DiscountType,
    decimal Value,
    string FundedBy = "Platform",
    decimal? MaxDiscount = null,
    decimal? MinOrderValue = null,
    DateTime? StartsAtUtc = null,
    DateTime? EndsAtUtc = null,
    int? TotalLimit = null,
    int? PerBuyerLimit = null)
{
    internal CreateCouponCommand ToCommand(Guid? sellerId, string fundedBy) => new(
        sellerId, Code, Description, fundedBy, DiscountType, Value, MaxDiscount, MinOrderValue, StartsAtUtc, EndsAtUtc, TotalLimit, PerBuyerLimit);
}
