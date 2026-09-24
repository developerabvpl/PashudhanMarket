using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Sellers.Contracts.Events;

/// <summary>Staff approved a seller's application: they can now list and sell. The seller is told.</summary>
public sealed record SellerApprovedDomainEvent(Guid SellerId, string ShopName) : DomainEvent;

/// <summary>Staff turned a seller's application down, saying why. The seller is told, and may correct it and apply again.</summary>
public sealed record SellerRejectedDomainEvent(Guid SellerId, string ShopName, string Note) : DomainEvent;
