using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Settlements.Domain;

/// <summary>Every failure this module can return.</summary>
public static class SettlementErrors
{
    public static readonly Error PayoutNotFound = Error.NotFound(
        "settlements.payout.not_found",
        "Payout not found.");

    public static readonly Error AlreadyPaid = Error.Conflict(
        "settlements.payout.already_paid",
        "This payout is already recorded as paid.");

    public static readonly Error RatesTooHigh = Error.Validation(
        "settlements.rates_too_high",
        "Commission and taxes together must come to less than 100%.");

    public static readonly Error SellerNotFound = Error.NotFound(
        "settlements.seller.not_found",
        "No seller with that id.");

    public static readonly Error ConcurrentChange = Error.Conflict(
        "settlements.concurrent_change",
        "This was changed by someone else at the same time. Reload and try again.");
}
