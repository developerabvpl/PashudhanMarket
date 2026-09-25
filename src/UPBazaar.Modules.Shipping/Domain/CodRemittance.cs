using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Shipping.Domain;

/// <summary>
/// One bank transfer from the courier paying over cash it collected, as its remittance report
/// lists it: a row per parcel. Kept whole, matched or not, so what the bank received can always be
/// traced to the parcels it paid for.
///
/// A row whose AWB matches no delivered cash-on-delivery parcel is kept unmatched: the courier may
/// have paid before its delivery update arrived. It is matched when that update comes.
/// </summary>
public sealed class CodRemittance : AggregateRoot
{
    public const int ReferenceMaxLength = 64;

    public const int FileNameMaxLength = 200;

    private readonly List<CodRemittanceLine> _lines = [];

    private CodRemittance()
    {
    }

    /// <summary>The transfer's bank reference (UTR). Unique, so the same report cannot be counted twice.</summary>
    public string Reference { get; private set; } = string.Empty;

    /// <summary>The day the money arrived.</summary>
    public DateOnly RemittedOn { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public DateTime UploadedAtUtc { get; private set; }

    public string? UploadedBy { get; private set; }

    public IReadOnlyCollection<CodRemittanceLine> Lines => _lines.AsReadOnly();

    public decimal Total => _lines.Sum(l => l.Amount);

    public static CodRemittance Create(
        string reference,
        DateOnly remittedOn,
        string fileName,
        IEnumerable<(string Awb, decimal Amount)> rows,
        string? uploadedBy,
        DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentNullException.ThrowIfNull(rows);

        var remittance = new CodRemittance
        {
            Reference = reference.Trim(),
            RemittedOn = remittedOn,
            FileName = fileName.Length > FileNameMaxLength ? fileName[..FileNameMaxLength] : fileName,
            UploadedAtUtc = now,
            UploadedBy = uploadedBy,
        };

        remittance._lines.AddRange(rows.Select(r => CodRemittanceLine.Create(r.Awb, r.Amount)));

        return remittance;
    }
}

/// <summary>One row of a remittance: what the courier paid for one parcel.</summary>
public sealed class CodRemittanceLine : Entity
{
    public const int AwbMaxLength = 64;

    private CodRemittanceLine()
    {
    }

    public long CodRemittanceId { get; private set; }

    public string Awb { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    /// <summary>Public id of the parcel's receivable it paid towards; null until matched.</summary>
    public Guid? ReceivableId { get; private set; }

    internal static CodRemittanceLine Create(string awb, decimal amount) => new() { Awb = awb.Trim(), Amount = amount };

    /// <summary>Pays this row's amount towards the parcel it names.</summary>
    public void MatchTo(CodReceivable receivable, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(receivable);

        if (ReceivableId is not null)
        {
            return;
        }

        ReceivableId = receivable.PublicId;
        receivable.Pay(Amount, now);
    }
}
