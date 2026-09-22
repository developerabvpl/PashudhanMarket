namespace UPBazaar.Modules.Catalog.Contracts.Dtos;

/// <summary>A category as the API exposes it.</summary>
/// <param name="Id">Public id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Slug">URL segment, unique.</param>
/// <param name="ParentId">Parent category, or null at the top level.</param>
public sealed record CategoryDto(Guid Id, string Name, string Slug, Guid? ParentId);

/// <summary>
/// One product in full.
///
/// The field names match the contract the storefront was written against, so its generated
/// client keeps working when it switches from the bundled catalogue to this API.
/// </summary>
public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    string? Brand,
    string? Description,
    decimal Price,
    string Currency,
    string Status,
    Guid SellerId,
    CategoryDto Category,
    int OnHandQuantity,
    int ReservedQuantity,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc,
    ProductPackageDto? Package,
    string? ReviewNote);

/// <summary>How one unit of a product ships, as packed. Couriers price on these.</summary>
/// <param name="WeightGrams">Packed weight in grams.</param>
/// <param name="LengthCm">Box length in centimetres.</param>
/// <param name="BreadthCm">Box breadth in centimetres.</param>
/// <param name="HeightCm">Box height in centimetres.</param>
public sealed record ProductPackageDto(int WeightGrams, decimal LengthCm, decimal BreadthCm, decimal HeightCm);

/// <summary>A product in a listing: enough for a card, nothing that needs a join.</summary>
public sealed record ProductSummaryDto(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    string Status,
    int AvailableQuantity);
