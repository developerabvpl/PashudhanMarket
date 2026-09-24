using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Reviews.Contracts.Events;

/// <summary>
/// A buyer reviewed a product for the first time. Raised once per review, not on edits, so the
/// seller hears about each buyer once.
/// </summary>
/// <param name="ReviewId">The review.</param>
/// <param name="SellerId">Whose product it is.</param>
/// <param name="ProductId">The product.</param>
/// <param name="ProductName">Its name when delivered.</param>
/// <param name="Rating">1 to 5 stars.</param>
/// <param name="HasContent">The buyer wrote words or added photos, which staff will check.</param>
public sealed record ReviewWrittenDomainEvent(
    Guid ReviewId,
    Guid SellerId,
    Guid ProductId,
    string ProductName,
    int Rating,
    bool HasContent) : DomainEvent;

/// <summary>
/// Staff kept a review's words and photos off the storefront. The buyer is told why, so they can
/// change them.
/// </summary>
/// <param name="ReviewId">The review.</param>
/// <param name="BuyerId">Who wrote it.</param>
/// <param name="OrderId">The order that delivered the product, whose page has the review on it.</param>
/// <param name="ProductName">The product's name when delivered.</param>
/// <param name="Note">What staff want changed.</param>
public sealed record ReviewRejectedDomainEvent(
    Guid ReviewId,
    Guid BuyerId,
    Guid OrderId,
    string ProductName,
    string Note) : DomainEvent;
