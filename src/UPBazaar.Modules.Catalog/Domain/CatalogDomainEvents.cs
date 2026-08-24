using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Catalog.Domain;

/// <summary>
/// Raised inside the module only. Cross-module events live in the Contracts project so other
/// modules can handle them without referencing this assembly.
/// </summary>
public sealed record ProductCreatedDomainEvent(Guid ProductId, string Sku, Guid SellerId) : DomainEvent;

public sealed record ProductPublishedDomainEvent(
    Guid ProductId,
    string Sku,
    decimal Price,
    string Currency) : DomainEvent;
