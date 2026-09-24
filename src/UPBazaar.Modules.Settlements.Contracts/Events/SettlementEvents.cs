using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Settlements.Contracts.Events;

/// <summary>Finance recorded a payout as transferred. The seller is told how much, for how many parcels, and the UTR.</summary>
public sealed record PayoutPaidDomainEvent(
    Guid PayoutId,
    Guid SellerId,
    decimal NetAmount,
    string Currency,
    int EarningCount,
    string Utr) : DomainEvent;
