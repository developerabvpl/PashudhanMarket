using UPBazaar.Modules.Settlements.Contracts.Dtos;
using UPBazaar.Modules.Settlements.Domain;

namespace UPBazaar.Modules.Settlements.Application;

/// <summary>Maps settlement entities to the DTOs the API exposes.</summary>
internal static class SettlementMappings
{
    public static SettlementPolicyDto ToDto(this SettlementPolicy policy) => new(
        policy.DefaultCommissionPercent,
        policy.TcsPercent,
        policy.TdsPercent,
        policy.ModifiedAtUtc,
        policy.ModifiedBy);

    /// <summary>Requires the earnings to be loaded.</summary>
    /// <param name="payout">The payout.</param>
    /// <param name="maskAccount">
    /// True for the seller, who knows their own account and gains nothing from seeing it whole;
    /// false for finance, who need it to make the transfer.
    /// </param>
    public static PayoutDto ToDto(this Payout payout, bool maskAccount) => new(
        payout.PublicId,
        payout.SellerId,
        payout.ShopName,
        payout.AccountHolder,
        maskAccount ? Mask(payout.AccountNumber) : payout.AccountNumber,
        payout.Ifsc,
        payout.GrossAmount,
        payout.CommissionAmount,
        payout.TcsAmount,
        payout.TdsAmount,
        payout.NetAmount,
        payout.Currency,
        payout.Status.ToString(),
        payout.CreatedAtUtc,
        payout.PaidAtUtc,
        payout.Utr,
        payout.PaidBy,
        [.. payout.Earnings.OrderBy(e => e.DeliveredAtUtc).Select(e => e.ToDto(payout.PublicId))]);

    public static EarningDto ToDto(this Earning earning, Guid? payoutId) => new(
        earning.PublicId,
        earning.SellerId,
        earning.OrderId,
        earning.OrderNumber,
        earning.OrderPartId,
        earning.GrossAmount,
        earning.CommissionPercent,
        earning.CommissionAmount,
        earning.TcsAmount,
        earning.TdsAmount,
        earning.NetAmount,
        earning.Currency,
        earning.Status.ToString(),
        earning.DeliveredAtUtc,
        earning.PayableFromUtc,
        payoutId);

    private static string Mask(string accountNumber) =>
        accountNumber.Length <= 4 ? accountNumber : new string('•', accountNumber.Length - 4) + accountNumber[^4..];
}
