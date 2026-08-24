using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Payments.Contracts.Events;

/// <summary>Money captured. Ordering listens for this to move the order out of AwaitingPayment.</summary>
public sealed record PaymentCapturedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency) : DomainEvent;

/// <summary>Capture failed or was abandoned. Ordering releases the stock it was holding.</summary>
public sealed record PaymentFailedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    string Reason) : DomainEvent;

public sealed record RefundIssuedDomainEvent(
    Guid RefundId,
    Guid PaymentId,
    Guid OrderId,
    decimal Amount) : DomainEvent;
