using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Catalog.Contracts.Events;

/// <summary>
/// A listing became visible to shoppers. Search indexing and the seller's notification hang
/// off this; neither exists yet, which is why the event is published now.
/// </summary>
public sealed record ProductPublishedDomainEvent(Guid ProductId, Guid SellerId, string Sku) : DomainEvent;

/// <summary>A listing was withdrawn. Carts holding it need to know it can no longer be bought.</summary>
public sealed record ProductArchivedDomainEvent(Guid ProductId, Guid SellerId, string Sku) : DomainEvent;

/// <summary>
/// The price changed. Carts re-price from this rather than trusting the figure they captured
/// when the item was added.
/// </summary>
public sealed record ProductPriceChangedDomainEvent(
    Guid ProductId,
    decimal OldPrice,
    decimal NewPrice,
    string Currency) : DomainEvent;

/// <summary>A moderator sent a seller's listing back as a draft, saying what to fix. The seller is told.</summary>
/// <param name="ProductId">The listing.</param>
/// <param name="SellerId">Whose it is.</param>
/// <param name="Name">The listing's title.</param>
/// <param name="Note">What to fix, in the moderator's words.</param>
public sealed record ProductSentBackDomainEvent(Guid ProductId, Guid SellerId, string Name, string Note) : DomainEvent;
