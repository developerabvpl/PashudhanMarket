using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Inventory.Domain;

/// <summary>Where a hold is in its life. Only <see cref="Active"/> ever changes.</summary>
public enum ReservationStatus
{
    Active = 0,
    Committed = 1,
    Released = 2,
    Expired = 3,
}

/// <summary>
/// Stock held for one checkout. It changes no quantities itself - <see cref="StockItem"/> does -
/// but it remembers what was held, so the hold can be undone or completed exactly.
/// </summary>
public sealed class Reservation : Entity
{
    private readonly List<ReservationLine> _lines = [];

    private Reservation()
    {
    }

    /// <summary>What the hold is for, such as an order number.</summary>
    public string Reference { get; private set; } = null!;

    public ReservationStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>When it stopped being active, however that happened.</summary>
    public DateTime? ClosedAtUtc { get; private set; }

    public IReadOnlyCollection<ReservationLine> Lines => _lines.AsReadOnly();

    public static Reservation Create(
        string reference,
        IEnumerable<(Guid ProductId, int Quantity)> lines,
        DateTime now,
        TimeSpan holdFor)
    {
        var reservation = new Reservation
        {
            Reference = reference,
            Status = ReservationStatus.Active,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + holdFor,
        };

        reservation._lines.AddRange(lines.Select(l => ReservationLine.Create(l.ProductId, l.Quantity)));

        return reservation;
    }

    /// <summary>Marks the hold finished. The caller has already moved the stock.</summary>
    public void Close(ReservationStatus outcome, DateTime now)
    {
        if (Status != ReservationStatus.Active)
        {
            throw new InvalidOperationException($"Reservation {PublicId} is already {Status}.");
        }

        if (outcome == ReservationStatus.Active)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), "A reservation cannot be closed as Active.");
        }

        Status = outcome;
        ClosedAtUtc = now;
    }
}

/// <summary>One product in a hold.</summary>
public sealed class ReservationLine : Entity
{
    private ReservationLine()
    {
    }

    public long ReservationId { get; private set; }

    public Guid ProductId { get; private set; }

    public int Quantity { get; private set; }

    internal static ReservationLine Create(Guid productId, int quantity) => new()
    {
        ProductId = productId,
        Quantity = quantity,
    };
}
