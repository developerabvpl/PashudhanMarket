using UPBazaar.Modules.Settlements.Contracts.Events;
using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Settlements.Domain;

/// <summary>Whether a payout's money has gone.</summary>
public enum PayoutStatus
{
    /// <summary>Worked out; waiting for finance to send the transfer.</summary>
    Pending = 0,

    /// <summary>Sent, with the bank's transaction reference recorded.</summary>
    Paid = 1,
}

/// <summary>
/// One transfer to one seller, covering every earning that was payable when the run made it.
///
/// Made by the weekly run and paid by hand: finance send the NEFT or IMPS transfer and record its
/// UTR here. The bank account is copied in when the payout is made, so the record shows exactly
/// where the money was sent even if the seller later changes their account.
/// </summary>
public sealed class Payout : AggregateRoot, IAuditable
{
    private readonly List<Earning> _earnings = [];

    private Payout()
    {
    }

    public Guid SellerId { get; private set; }

    public string ShopName { get; private set; } = string.Empty;

    public string AccountHolder { get; private set; } = string.Empty;

    public string AccountNumber { get; private set; } = string.Empty;

    public string Ifsc { get; private set; } = string.Empty;

    public string Currency { get; private set; } = string.Empty;

    public decimal GrossAmount { get; private set; }

    public decimal CommissionAmount { get; private set; }

    public decimal TcsAmount { get; private set; }

    public decimal TdsAmount { get; private set; }

    /// <summary>What is transferred.</summary>
    public decimal NetAmount { get; private set; }

    public int EarningCount { get; private set; }

    public PayoutStatus Status { get; private set; }

    /// <summary>The bank's transaction reference for the transfer, once sent.</summary>
    public string? Utr { get; private set; }

    public DateTime? PaidAtUtc { get; private set; }

    /// <summary>User id of whoever recorded it as paid.</summary>
    public string? PaidBy { get; private set; }

    public IReadOnlyCollection<Earning> Earnings => _earnings.AsReadOnly();

    /// <summary>Optimistic concurrency: two people recording the same transfer must not both win.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>Gathers one seller's payable earnings into a payout and marks them settled.</summary>
    public static Payout Create(
        Guid sellerId,
        string shopName,
        string accountHolder,
        string accountNumber,
        string ifsc,
        IReadOnlyCollection<Earning> earnings)
    {
        ArgumentNullException.ThrowIfNull(earnings);

        if (earnings.Count == 0 || earnings.Any(e => e.SellerId != sellerId))
        {
            throw new ArgumentException("A payout needs at least one earning, all the seller's own.", nameof(earnings));
        }

        var payout = new Payout
        {
            SellerId = sellerId,
            ShopName = shopName,
            AccountHolder = accountHolder,
            AccountNumber = accountNumber,
            Ifsc = ifsc,
            Currency = earnings.First().Currency,
            GrossAmount = earnings.Sum(e => e.GrossAmount),
            CommissionAmount = earnings.Sum(e => e.CommissionAmount),
            TcsAmount = earnings.Sum(e => e.TcsAmount),
            TdsAmount = earnings.Sum(e => e.TdsAmount),
            NetAmount = earnings.Sum(e => e.NetAmount),
            EarningCount = earnings.Count,
            Status = PayoutStatus.Pending,
        };

        foreach (var earning in earnings)
        {
            earning.Settle();
            payout._earnings.Add(earning);
        }

        return payout;
    }

    public Result MarkPaid(string utr, string? paidBy, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(utr);

        if (Status == PayoutStatus.Paid)
        {
            return Result.Failure(SettlementErrors.AlreadyPaid);
        }

        Status = PayoutStatus.Paid;
        Utr = utr.Trim();
        PaidAtUtc = now;
        PaidBy = paidBy;

        Raise(new PayoutPaidDomainEvent(PublicId, SellerId, NetAmount, Currency, EarningCount, Utr));

        return Result.Success();
    }
}
