using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Inventory.Contracts.Events;

/// <summary>
/// Nothing is left to sell. The storefront's sold-out badge and a seller's restock alert hang
/// off this; neither is wired yet, which is why the event is published now.
/// </summary>
public sealed record StockDepletedDomainEvent(Guid ProductId) : DomainEvent;

/// <summary>A product that had sold out can be bought again. "Notify me" requests fire from this.</summary>
public sealed record StockReplenishedDomainEvent(Guid ProductId, int AvailableQuantity) : DomainEvent;
