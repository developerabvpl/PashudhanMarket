using UPBazaar.Modules.Inventory.Contracts.Events;
using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Inventory.Domain;

/// <summary>What happened to stock.</summary>
public enum StockMovementType
{
    /// <summary>A delivery arrived.</summary>
    Received = 0,

    /// <summary>Stock was lost, damaged or expired.</summary>
    WrittenOff = 1,

    /// <summary>A stock-take replaced the recorded figure with the counted one.</summary>
    Counted = 2,

    /// <summary>Held for a checkout.</summary>
    Reserved = 3,

    /// <summary>A hold ended without a sale and the stock went back on sale.</summary>
    Released = 4,

    /// <summary>A held quantity left the building.</summary>
    Committed = 5,
}

/// <summary>
/// One product's stock, and the only place its numbers change.
///
/// Every change appends a <see cref="StockMovement"/>, so the current figures can always be
/// explained line by line. Nothing edits a movement after the fact: a mistake is corrected by a
/// further movement, the way a ledger is.
///
/// <see cref="RowVersion"/> makes two checkouts racing for the last unit fail loudly rather than
/// both succeeding: whichever saves second gets a concurrency conflict instead of overselling.
/// </summary>
public sealed class StockItem : AggregateRoot, IAuditable
{
    private readonly List<StockMovement> _movements = [];

    private StockItem()
    {
    }

    /// <summary>Catalog's public id for the product. A plain id: no key crosses a module boundary.</summary>
    public Guid ProductId { get; private set; }

    public int OnHandQuantity { get; private set; }

    public int ReservedQuantity { get; private set; }

    public int AvailableQuantity => OnHandQuantity - ReservedQuantity;

    /// <summary>Optimistic concurrency token, maintained by SQL Server.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Movements added since this item was loaded. The full ledger is queried, not loaded.</summary>
    public IReadOnlyCollection<StockMovement> Movements => _movements.AsReadOnly();

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>An item with nothing in stock, ready for its first movement.</summary>
    public static StockItem Create(Guid productId) => new() { ProductId = productId };

    public Result Receive(int quantity, string? reason, string? reference, DateTime now, string? actor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        return Apply(StockMovementType.Received, quantity, 0, reason, reference, now, actor);
    }

    public Result WriteOff(int quantity, string reason, DateTime now, string? actor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        return Apply(StockMovementType.WrittenOff, -quantity, 0, reason, null, now, actor);
    }

    /// <summary>Sets the recorded figure to what a stock-take found. Records the difference, not the count.</summary>
    public Result Count(int countedOnHand, string? reason, DateTime now, string? actor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(countedOnHand);

        return Apply(StockMovementType.Counted, countedOnHand - OnHandQuantity, 0, reason, null, now, actor);
    }

    public Result Reserve(int quantity, string reference, DateTime now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        return quantity > AvailableQuantity
            ? Result.Failure(InventoryErrors.InsufficientStock)
            : Apply(StockMovementType.Reserved, 0, quantity, null, reference, now, actor: null);
    }

    public Result Release(int quantity, string reference, DateTime now) =>
        Apply(StockMovementType.Released, 0, -quantity, null, reference, now, actor: null);

    /// <summary>A held quantity has gone: it leaves both stock on hand and the reserved pile.</summary>
    public Result Commit(int quantity, string reference, DateTime now) =>
        Apply(StockMovementType.Committed, -quantity, -quantity, null, reference, now, actor: null);

    private Result Apply(
        StockMovementType type,
        int onHandChange,
        int reservedChange,
        string? reason,
        string? reference,
        DateTime now,
        string? actor)
    {
        var onHand = OnHandQuantity + onHandChange;
        var reserved = ReservedQuantity + reservedChange;

        if (reserved < 0)
        {
            // Only reachable through a bug: a release or commit for more than was ever held.
            throw new InvalidOperationException(
                $"Stock for product {ProductId} would have {reserved} reserved after a {type}.");
        }

        if (onHand < reserved)
        {
            return Result.Failure(InventoryErrors.BelowReserved);
        }

        var wasAvailable = AvailableQuantity > 0;

        OnHandQuantity = onHand;
        ReservedQuantity = reserved;

        _movements.Add(StockMovement.Create(
            type, onHandChange, reservedChange, onHand, reason, reference, now, actor));

        var isAvailable = AvailableQuantity > 0;

        if (wasAvailable && !isAvailable)
        {
            Raise(new StockDepletedDomainEvent(ProductId));
        }
        else if (!wasAvailable && isAvailable)
        {
            Raise(new StockReplenishedDomainEvent(ProductId, AvailableQuantity));
        }

        return Result.Success();
    }
}

/// <summary>One line in a product's stock ledger. Written once, never changed.</summary>
public sealed class StockMovement : Entity
{
    private StockMovement()
    {
    }

    public long StockItemId { get; private set; }

    public StockMovementType Type { get; private set; }

    public int OnHandChange { get; private set; }

    public int ReservedChange { get; private set; }

    /// <summary>Stock on hand once this movement applied, so the ledger reads without a running sum.</summary>
    public int OnHandAfter { get; private set; }

    public string? Reason { get; private set; }

    public string? Reference { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>User id of whoever recorded it; null for movements the system made itself.</summary>
    public string? RecordedBy { get; private set; }

    internal static StockMovement Create(
        StockMovementType type,
        int onHandChange,
        int reservedChange,
        int onHandAfter,
        string? reason,
        string? reference,
        DateTime occurredAtUtc,
        string? recordedBy) => new()
    {
        Type = type,
        OnHandChange = onHandChange,
        ReservedChange = reservedChange,
        OnHandAfter = onHandAfter,
        Reason = reason,
        Reference = reference,
        OccurredAtUtc = occurredAtUtc,
        RecordedBy = recordedBy,
    };
}
