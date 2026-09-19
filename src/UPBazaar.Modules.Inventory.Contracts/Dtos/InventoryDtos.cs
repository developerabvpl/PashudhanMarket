namespace UPBazaar.Modules.Inventory.Contracts.Dtos;

/// <summary>How much of one product there is.</summary>
/// <param name="ProductId">Catalog public id.</param>
/// <param name="OnHandQuantity">Physically in stock.</param>
/// <param name="ReservedQuantity">Held for carts and orders that have not completed.</param>
/// <param name="AvailableQuantity">What a shopper can still buy: on hand less reserved.</param>
public sealed record StockLevelDto(
    Guid ProductId,
    int OnHandQuantity,
    int ReservedQuantity,
    int AvailableQuantity)
{
    /// <summary>The level of a product Inventory has never counted: nothing of it exists.</summary>
    public static StockLevelDto None(Guid productId) => new(productId, 0, 0, 0);
}

/// <summary>One entry in a product's stock ledger.</summary>
/// <param name="Id">Public id of the movement.</param>
/// <param name="Type">Received, WrittenOff, Counted, Reserved, Released or Committed.</param>
/// <param name="OnHandChange">Change to stock on hand; zero for reservations.</param>
/// <param name="ReservedChange">Change to reserved stock; zero for deliveries and counts.</param>
/// <param name="OnHandAfter">Stock on hand once this movement applied.</param>
/// <param name="Reason">Free text from whoever recorded it.</param>
/// <param name="Reference">Order, cart or document the movement belongs to, if any.</param>
/// <param name="OccurredAtUtc">When it was recorded.</param>
/// <param name="RecordedBy">User id of whoever recorded it; null for the system's own movements.</param>
public sealed record StockMovementDto(
    Guid Id,
    string Type,
    int OnHandChange,
    int ReservedChange,
    int OnHandAfter,
    string? Reason,
    string? Reference,
    DateTime OccurredAtUtc,
    string? RecordedBy);

/// <summary>A stock level with its most recent history.</summary>
public sealed record StockDetailDto(StockLevelDto Level, IReadOnlyList<StockMovementDto> RecentMovements);

/// <summary>One product and quantity to hold.</summary>
public sealed record ReservationLineDto(Guid ProductId, int Quantity);
