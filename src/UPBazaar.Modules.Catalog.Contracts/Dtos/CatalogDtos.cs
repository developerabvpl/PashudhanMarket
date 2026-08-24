namespace UPBazaar.Modules.Catalog.Contracts.Dtos;

public sealed record CategoryDto(Guid Id, string Name, string Slug, Guid? ParentId);

public sealed record ProductSummaryDto(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    string Status,
    int AvailableQuantity);

public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    string? Description,
    decimal Price,
    string Currency,
    string Status,
    Guid SellerId,
    CategoryDto Category,
    int OnHandQuantity,
    int ReservedQuantity,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc);

/// <summary>One line of a stock reservation request, keyed on the product public id.</summary>
public sealed record StockReservationLine(Guid ProductId, int Quantity);

/// <summary>
/// What Ordering needs back to build an order line without ever reading catalog tables.
/// </summary>
public sealed record ReservedItemDto(
    Guid ProductId,
    string Sku,
    string Name,
    decimal UnitPrice,
    string Currency,
    int Quantity);
