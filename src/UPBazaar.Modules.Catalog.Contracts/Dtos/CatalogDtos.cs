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

/// <summary>
/// A product as a row on the staff catalogue screen: who sells it, where it is shelved, and
/// whether it is ready to ship, so a moderator can spot what needs attention without opening it.
/// </summary>
/// <param name="Id">Public id.</param>
/// <param name="Sku">Stock-keeping unit.</param>
/// <param name="Name">Listing title.</param>
/// <param name="Price">Price in <paramref name="Currency"/>.</param>
/// <param name="Currency">Always INR today.</param>
/// <param name="Status">Draft, InReview, Active or Archived.</param>
/// <param name="CategoryId">Category it is listed under.</param>
/// <param name="CategoryName">That category's name.</param>
/// <param name="SellerId">Public id of the selling account.</param>
/// <param name="SellerName">The seller's shop name, or null if the id matches no seller.</param>
/// <param name="AvailableQuantity">On hand less what is held for checkouts.</param>
/// <param name="HasPackage">Whether its packed weight and size are recorded, which couriers need.</param>
/// <param name="UpdatedAtUtc">Last change, or creation if it was never changed.</param>
public sealed record AdminProductSummaryDto(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    string Status,
    Guid CategoryId,
    string CategoryName,
    Guid SellerId,
    string? SellerName,
    int AvailableQuantity,
    bool HasPackage,
    DateTime UpdatedAtUtc);

/// <summary>A product in a listing: enough for a card, nothing that needs a join.</summary>
public sealed record ProductSummaryDto(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    string Status,
    int AvailableQuantity);
