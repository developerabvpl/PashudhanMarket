using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Payments.Domain;

public enum SettlementStatus
{
    Pending = 0,
    Settled = 1,
    Reversed = 2,
}

/// <summary>
/// What a seller is owed for one captured payment. Carries a rowversion because a payout run
/// and a refund can touch the same line at the same time.
/// </summary>
public sealed class SettlementLine : Entity, IAuditable
{
    private SettlementLine()
    {
    }

    public long PaymentId { get; private set; }

    public Guid SellerId { get; private set; }

    public decimal GrossAmount { get; private set; }

    public decimal CommissionAmount { get; private set; }

    public decimal NetAmount { get; private set; }

    public string Currency { get; private set; } = "INR";

    public SettlementStatus Status { get; private set; }

    public DateTime? SettledAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    internal static SettlementLine Create(
        Guid sellerId,
        decimal grossAmount,
        decimal commissionRate,
        string currency)
    {
        var commission = decimal.Round(grossAmount * commissionRate, 2, MidpointRounding.AwayFromZero);

        return new SettlementLine
        {
            SellerId = sellerId,
            GrossAmount = grossAmount,
            CommissionAmount = commission,
            NetAmount = grossAmount - commission,
            Currency = currency,
            Status = SettlementStatus.Pending,
        };
    }

    public void MarkSettled(DateTime settledAtUtc)
    {
        Status = SettlementStatus.Settled;
        SettledAtUtc = settledAtUtc;
    }

    public void Reverse() => Status = SettlementStatus.Reversed;
}
