using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Settlements.Domain;

/// <summary>
/// The rates every earning is worked out at: the platform's commission, and the two taxes a
/// marketplace withholds from what it pays sellers - GST TCS and income-tax TDS (section 194-O).
///
/// One row for the whole platform. A change applies to sales delivered from then on; each earning
/// copies the rates it was worked out at, so past earnings never move. The tax rates start at zero
/// on purpose: what they should be is for the platform's accountant to say, not a default here.
/// </summary>
public sealed class SettlementPolicy : Entity, IAuditable
{
    /// <summary>Commission a new platform starts with, until staff set their own.</summary>
    public const decimal StartingCommissionPercent = 10m;

    private SettlementPolicy()
    {
    }

    /// <summary>Commission on a seller's sales, unless they have a rate of their own.</summary>
    public decimal DefaultCommissionPercent { get; private set; }

    /// <summary>GST tax collected at source, as a percentage of the sale.</summary>
    public decimal TcsPercent { get; private set; }

    /// <summary>Income tax deducted at source under section 194-O, as a percentage of the sale.</summary>
    public decimal TdsPercent { get; private set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public static SettlementPolicy CreateDefault() => new()
    {
        DefaultCommissionPercent = StartingCommissionPercent,
        TcsPercent = 0m,
        TdsPercent = 0m,
    };

    public Result Change(decimal defaultCommissionPercent, decimal tcsPercent, decimal tdsPercent)
    {
        // Keeps the default from paying sellers nothing. Sellers with their own commission are
        // checked against the tax rates by the handlers that change either.
        if (defaultCommissionPercent + tcsPercent + tdsPercent >= 100m)
        {
            return Result.Failure(SettlementErrors.RatesTooHigh);
        }

        DefaultCommissionPercent = defaultCommissionPercent;
        TcsPercent = tcsPercent;
        TdsPercent = tdsPercent;

        return Result.Success();
    }
}

/// <summary>A seller's own commission, agreed with them, in place of the platform default.</summary>
public sealed class SellerCommission : Entity, IAuditable
{
    private SellerCommission()
    {
    }

    public Guid SellerId { get; private set; }

    public decimal CommissionPercent { get; private set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public static SellerCommission Create(Guid sellerId, decimal commissionPercent) => new()
    {
        SellerId = sellerId,
        CommissionPercent = commissionPercent,
    };

    public void Change(decimal commissionPercent) => CommissionPercent = commissionPercent;
}
