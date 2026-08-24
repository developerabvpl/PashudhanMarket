using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Identity.Contracts.Events;

/// <summary>
/// A new account exists. Crm creates a customer record from this; Notifications sends the
/// welcome message. Neither is wired yet, which is exactly why the event is published now
/// rather than when someone needs it.
/// </summary>
public sealed record UserRegisteredDomainEvent(
    Guid UserId,
    string UserType,
    string? Email,
    string? Mobile,
    string DisplayName) : DomainEvent;

/// <summary>An account was locked out after repeated failed sign-ins.</summary>
public sealed record UserLockedOutDomainEvent(Guid UserId, DateTime LockoutEndsAtUtc) : DomainEvent;

/// <summary>
/// A refresh token was presented twice. Either the network replayed it or a token was stolen;
/// the module assumes theft and revokes the family.
/// </summary>
public sealed record RefreshTokenReuseDetectedDomainEvent(
    Guid UserId,
    DateTime DetectedAtUtc,
    string? IpAddress) : DomainEvent;
