namespace UPBazaar.Modules.Settlements.Contracts.Dtos;

/// <summary>The rates new earnings are worked out at.</summary>
/// <param name="DefaultCommissionPercent">Commission for sellers without a rate of their own.</param>
/// <param name="TcsPercent">GST tax collected at source, withheld from every sale.</param>
/// <param name="TdsPercent">Income tax deducted at source (section 194-O), withheld from every sale.</param>
/// <param name="ModifiedAtUtc">When the rates last changed; null if never.</param>
/// <param name="ModifiedBy">Who changed them.</param>
public sealed record SettlementPolicyDto(
    decimal DefaultCommissionPercent,
    decimal TcsPercent,
    decimal TdsPercent,
    DateTime? ModifiedAtUtc,
    string? ModifiedBy);

/// <summary>A seller's own commission, in place of the default.</summary>
/// <param name="SellerId">The seller.</param>
/// <param name="ShopName">Their shop name, or null if the id matches no seller.</param>
/// <param name="CommissionPercent">Their rate.</param>
public sealed record SellerCommissionDto(Guid SellerId, string? ShopName, decimal CommissionPercent);

/// <summary>What a seller earns from one delivered parcel.</summary>
/// <param name="Id">Public id.</param>
/// <param name="SellerId">The seller.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="OrderPartId">The parcel.</param>
/// <param name="Kind">
/// Sale (the goods), Delivery (the seller's share of the delivery charge, without commission) or
/// CourierCost (a courier trip charged to the seller, taken off their pay).
/// </param>
/// <param name="Detail">For a courier cost, the trip: Delivery, Rto or ReturnPickup.</param>
/// <param name="GrossAmount">What the buyer paid for its goods, or for its delivery; negative for a courier cost.</param>
/// <param name="CommissionPercent">Commission rate it was worked out at.</param>
/// <param name="CommissionAmount">Commission taken.</param>
/// <param name="TcsAmount">GST TCS withheld.</param>
/// <param name="TdsAmount">Income-tax TDS withheld.</param>
/// <param name="NetAmount">What the seller is paid for it.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="Status">Accruing (payable after <paramref name="PayableFromUtc"/>), OnHold (a return is asked for), Cancelled (returned) or Settled (in a payout).</param>
/// <param name="DeliveredAtUtc">When the parcel was delivered, or the courier trip made.</param>
/// <param name="PayableFromUtc">When the buyer's return window closes; for a courier cost, at once.</param>
/// <param name="PayoutId">The payout it was paid in, once settled.</param>
/// <param name="AwaitingCash">A cash-on-delivery parcel whose cash the courier has not paid over yet: not payable until it has.</param>
public sealed record EarningDto(
    Guid Id,
    Guid SellerId,
    Guid OrderId,
    string OrderNumber,
    Guid OrderPartId,
    string Kind,
    string? Detail,
    decimal GrossAmount,
    decimal CommissionPercent,
    decimal CommissionAmount,
    decimal TcsAmount,
    decimal TdsAmount,
    decimal NetAmount,
    string Currency,
    string Status,
    DateTime DeliveredAtUtc,
    DateTime PayableFromUtc,
    Guid? PayoutId,
    bool AwaitingCash);

/// <summary>A payout in a list.</summary>
/// <param name="Id">Public id.</param>
/// <param name="SellerId">Who is paid.</param>
/// <param name="ShopName">Their shop name when the payout was made.</param>
/// <param name="GrossAmount">Sales it covers.</param>
/// <param name="NetAmount">What is transferred.</param>
/// <param name="EarningCount">Parcels it covers.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="Status">Pending (to be transferred) or Paid.</param>
/// <param name="CreatedAtUtc">When the run made it.</param>
/// <param name="PaidAtUtc">When it was recorded as paid.</param>
/// <param name="Utr">The bank's transaction reference, once paid.</param>
public sealed record PayoutSummaryDto(
    Guid Id,
    Guid SellerId,
    string ShopName,
    decimal GrossAmount,
    decimal NetAmount,
    int EarningCount,
    string Currency,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    string? Utr);

/// <summary>
/// A payout in full: where the money goes and the earnings it covers. Staff see the account
/// number whole, to make the transfer; the seller sees it masked.
/// </summary>
/// <param name="Id">Public id.</param>
/// <param name="SellerId">Who is paid.</param>
/// <param name="ShopName">Their shop name when the payout was made.</param>
/// <param name="AccountHolder">Name on the account.</param>
/// <param name="AccountNumber">The account, as copied when the payout was made.</param>
/// <param name="Ifsc">The branch's IFSC.</param>
/// <param name="GrossAmount">Sales it covers.</param>
/// <param name="CommissionAmount">Commission taken.</param>
/// <param name="TcsAmount">GST TCS withheld.</param>
/// <param name="TdsAmount">Income-tax TDS withheld.</param>
/// <param name="CourierCostAmount">Courier charges taken off.</param>
/// <param name="NetAmount">What is transferred.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="Status">Pending or Paid.</param>
/// <param name="CreatedAtUtc">When the run made it.</param>
/// <param name="PaidAtUtc">When it was recorded as paid.</param>
/// <param name="Utr">The bank's transaction reference, once paid.</param>
/// <param name="PaidBy">Who recorded it as paid.</param>
/// <param name="Earnings">The parcels it covers.</param>
public sealed record PayoutDto(
    Guid Id,
    Guid SellerId,
    string ShopName,
    string AccountHolder,
    string AccountNumber,
    string Ifsc,
    decimal GrossAmount,
    decimal CommissionAmount,
    decimal TcsAmount,
    decimal TdsAmount,
    decimal CourierCostAmount,
    decimal NetAmount,
    string Currency,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    string? Utr,
    string? PaidBy,
    IReadOnlyList<EarningDto> Earnings);

/// <summary>A seller's money at a glance.</summary>
/// <param name="AccruingAmount">Delivered, waiting for the return window to close or, for cash on delivery, for the courier to pay the cash over.</param>
/// <param name="OnHoldAmount">Waiting for a return request to be decided.</param>
/// <param name="PayableAmount">Window closed: goes into the next payout run. Less than nothing when courier charges, owed at once, outweigh what is ready: the run then pays nothing and carries them into later payouts, and a client shows a payout of nothing with the shortfall carried forward.</param>
/// <param name="PendingPayoutAmount">In a payout not yet transferred.</param>
/// <param name="PaidAmount">Transferred, all time.</param>
/// <param name="Currency">ISO currency code.</param>
public sealed record SellerBalanceDto(
    decimal AccruingAmount,
    decimal OnHoldAmount,
    decimal PayableAmount,
    decimal PendingPayoutAmount,
    decimal PaidAmount,
    string Currency);

/// <summary>What a payout run did.</summary>
/// <param name="PayoutsCreated">Payouts made, one per seller with anything payable.</param>
/// <param name="EarningsSettled">Earnings those payouts cover.</param>
/// <param name="SellersSkipped">Sellers owed money but with no seller record to pay to; their earnings wait.</param>
/// <param name="SellersCarriedForward">Sellers whose courier costs came to as much as they had earned; it all waits for their next earnings.</param>
public sealed record PayoutRunResultDto(int PayoutsCreated, int EarningsSettled, int SellersSkipped, int SellersCarriedForward);
