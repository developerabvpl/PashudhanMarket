using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.Modules.Sellers.Contracts.Dtos;
using UPBazaar.Modules.Settlements.Application;
using UPBazaar.Modules.Settlements.Contracts.Dtos;
using UPBazaar.Modules.Settlements.Contracts.Permissions;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Settlements.Api;

/// <summary>
/// Paying sellers, for finance: the rates, the weekly payouts, and recording each transfer.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/settlements")]
[Produces("application/json")]
public sealed class AdminSettlementsController(IDispatcher dispatcher, ISellerDirectory sellers) : ControllerBase
{
    /// <summary>Lists the sellers a commission can be set for.</summary>
    [HttpGet("sellers")]
    [Authorize(SettlementsPermissions.PolicyWrite)]
    [EndpointSummary("List sellers for commissions")]
    [EndpointDescription("Every approved seller by shop name, for choosing whose commission to set. Names only.")]
    [ProducesResponseType<IReadOnlyList<SellerNameDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SellerNameDto>>> ListSellers(CancellationToken cancellationToken) =>
        Ok(await sellers.ListApprovedAsync(cancellationToken));

    /// <summary>Returns the commission and tax rates.</summary>
    [HttpGet("policy")]
    [Authorize(SettlementsPermissions.Read)]
    [EndpointSummary("Get settlement rates")]
    [ProducesResponseType<SettlementPolicyDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SettlementPolicyDto>> GetPolicy(CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetSettlementPolicyQuery(), cancellationToken)).ToActionResult();

    /// <summary>Changes the commission and tax rates.</summary>
    [HttpPut("policy")]
    [Authorize(SettlementsPermissions.PolicyWrite)]
    [EndpointSummary("Change settlement rates")]
    [EndpointDescription(
        "Sets the default commission and the GST TCS and income-tax TDS percentages withheld from "
        + "sellers. Applies to parcels delivered from now on; earnings already made keep their rates. "
        + "Commission and taxes together must stay under 100% for every seller.")]
    [ProducesResponseType<SettlementPolicyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SettlementPolicyDto>> UpdatePolicy(
        SettlementPolicyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(
                new UpdateSettlementPolicyCommand(request.DefaultCommissionPercent, request.TcsPercent, request.TdsPercent),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Lists sellers with a commission of their own.</summary>
    [HttpGet("commissions")]
    [Authorize(SettlementsPermissions.Read)]
    [EndpointSummary("List seller commissions")]
    [ProducesResponseType<IReadOnlyList<SellerCommissionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SellerCommissionDto>>> ListCommissions(CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new ListSellerCommissionsQuery(), cancellationToken)).ToActionResult();

    /// <summary>Sets a seller's own commission.</summary>
    [HttpPut("commissions/{sellerId:guid}")]
    [Authorize(SettlementsPermissions.PolicyWrite)]
    [EndpointSummary("Set a seller's commission")]
    [EndpointDescription("Replaces the default commission for this seller's future sales.")]
    [ProducesResponseType<SellerCommissionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SellerCommissionDto?>> SetCommission(
        Guid sellerId,
        SellerCommissionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(new SetSellerCommissionCommand(sellerId, request.CommissionPercent), cancellationToken))
            .ToActionResult();
    }

    /// <summary>Puts a seller back on the default commission.</summary>
    [HttpDelete("commissions/{sellerId:guid}")]
    [Authorize(SettlementsPermissions.PolicyWrite)]
    [EndpointSummary("Remove a seller's commission")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveCommission(Guid sellerId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new SetSellerCommissionCommand(sellerId, null), cancellationToken);

        return result.IsSuccess ? NoContent() : Result.Failure(result.Error).ToActionResult();
    }

    /// <summary>Lists payouts.</summary>
    [HttpGet("payouts")]
    [Authorize(SettlementsPermissions.Read)]
    [EndpointSummary("List payouts")]
    [EndpointDescription("Filter to Pending for the transfers still to make, oldest first.")]
    [ProducesResponseType<PagedList<PayoutSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedList<PayoutSummaryDto>>> ListPayouts(
        [FromQuery] PayoutListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.QueryAsync(
                new ListPayoutsQuery(request.Page ?? 1, request.PageSize ?? 25, request.Status, request.SellerId),
                cancellationToken))
            .ToActionResult();
    }

    /// <summary>Returns one payout, with the full bank account to pay it to.</summary>
    [HttpGet("payouts/{payoutId:guid}")]
    [Authorize(SettlementsPermissions.Read)]
    [EndpointSummary("Get a payout")]
    [ProducesResponseType<PayoutDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PayoutDto>> GetPayout(Guid payoutId, CancellationToken cancellationToken) =>
        (await dispatcher.QueryAsync(new GetPayoutQuery(payoutId, SellerId: null), cancellationToken)).ToActionResult();

    /// <summary>Records a payout as transferred.</summary>
    [HttpPost("payouts/{payoutId:guid}/mark-paid")]
    [Authorize(SettlementsPermissions.PayoutsApprove)]
    [EndpointSummary("Record a payout as paid")]
    [EndpointDescription("After sending the NEFT or IMPS transfer, record the UTR the bank gave for it.")]
    [ProducesResponseType<PayoutDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PayoutDto>> MarkPaid(
        Guid payoutId,
        MarkPayoutPaidRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.SendAsync(new MarkPayoutPaidCommand(payoutId, request.Utr), cancellationToken)).ToActionResult();
    }

    /// <summary>Makes payouts now.</summary>
    [HttpPost("payout-runs")]
    [Authorize(SettlementsPermissions.PayoutsApprove)]
    [EndpointSummary("Run payouts now")]
    [EndpointDescription(
        "Does now what the weekly run does on Monday morning: one payout per seller, covering every "
        + "earning whose return window has closed. Running it again finds nothing new.")]
    [ProducesResponseType<PayoutRunResultDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PayoutRunResultDto>> RunPayouts(CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new RunPayoutsCommand(), cancellationToken)).ToActionResult();

    /// <summary>Lists earnings.</summary>
    [HttpGet("earnings")]
    [Authorize(SettlementsPermissions.Read)]
    [EndpointSummary("List earnings")]
    [ProducesResponseType<PagedList<EarningDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedList<EarningDto>>> ListEarnings(
        [FromQuery] EarningListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await dispatcher.QueryAsync(
                new ListEarningsQuery(request.Page ?? 1, request.PageSize ?? 25, request.Status, request.SellerId),
                cancellationToken))
            .ToActionResult();
    }
}

/// <summary>
/// A seller's own money: what is coming, what has been paid. The seller is found from the token.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/seller/settlements")]
[Produces("application/json")]
[Authorize(SettlementsPermissions.OwnRead)]
public sealed class SellerSettlementsController(IDispatcher dispatcher, ICurrentUser currentUser, ISellerDirectory sellers)
    : ControllerBase
{
    /// <summary>Returns the seller's balance.</summary>
    [HttpGet("balance")]
    [EndpointSummary("My balance")]
    [EndpointDescription("What is waiting for return windows to close, on hold for returns, payable next Monday, being paid and paid.")]
    [ProducesResponseType<SellerBalanceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<SellerBalanceDto>> Balance(CancellationToken cancellationToken) =>
        AsSeller(seller => dispatcher.QueryAsync(new GetSellerBalanceQuery(seller), cancellationToken));

    /// <summary>Lists the seller's earnings.</summary>
    [HttpGet("earnings")]
    [EndpointSummary("My earnings")]
    [ProducesResponseType<PagedList<EarningDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<PagedList<EarningDto>>> Earnings(
        [FromQuery] SellerEarningListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return AsSeller(seller => dispatcher.QueryAsync(
            new ListEarningsQuery(request.Page ?? 1, request.PageSize ?? 25, request.Status, seller), cancellationToken));
    }

    /// <summary>Lists the seller's payouts.</summary>
    [HttpGet("payouts")]
    [EndpointSummary("My payouts")]
    [ProducesResponseType<PagedList<PayoutSummaryDto>>(StatusCodes.Status200OK)]
    public Task<ActionResult<PagedList<PayoutSummaryDto>>> Payouts(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        AsSeller(seller => dispatcher.QueryAsync(new ListPayoutsQuery(page ?? 1, pageSize ?? 25, Status: null, seller), cancellationToken));

    /// <summary>Returns one of the seller's payouts, with the parcels it covers.</summary>
    [HttpGet("payouts/{payoutId:guid}")]
    [EndpointSummary("Get my payout")]
    [ProducesResponseType<PayoutDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<PayoutDto>> Payout(Guid payoutId, CancellationToken cancellationToken) =>
        AsSeller(seller => dispatcher.QueryAsync(new GetPayoutQuery(payoutId, seller), cancellationToken));

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

/// <param name="DefaultCommissionPercent">Commission for sellers without their own, 0 to 100, two decimals.</param>
/// <param name="TcsPercent">GST TCS withheld, 0 to 100, two decimals. As your accountant advises.</param>
/// <param name="TdsPercent">Income-tax TDS (section 194-O) withheld, 0 to 100, two decimals. As your accountant advises.</param>
public sealed record SettlementPolicyRequest(decimal DefaultCommissionPercent, decimal TcsPercent, decimal TdsPercent);

/// <param name="CommissionPercent">The seller's rate, 0 to 100, two decimals.</param>
public sealed record SellerCommissionRequest(decimal CommissionPercent);

/// <param name="Utr">The bank's transaction reference for the transfer.</param>
public sealed record MarkPayoutPaidRequest(string Utr);

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 25.</param>
/// <param name="Status">Pending or Paid. All when omitted.</param>
/// <param name="SellerId">Only this seller's payouts.</param>
public sealed record PayoutListRequest(int? Page, int? PageSize, string? Status, Guid? SellerId);

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 25.</param>
/// <param name="Status">Accruing, OnHold, Cancelled or Settled. All when omitted.</param>
/// <param name="SellerId">Only this seller's earnings.</param>
public sealed record EarningListRequest(int? Page, int? PageSize, string? Status, Guid? SellerId);

/// <param name="Page">1-based page number. Defaults to 1.</param>
/// <param name="PageSize">Items per page, 1 to 100. Defaults to 25.</param>
/// <param name="Status">Accruing, OnHold, Cancelled or Settled. All when omitted.</param>
public sealed record SellerEarningListRequest(int? Page, int? PageSize, string? Status);
