using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Payments.Contracts.Events;

/// <summary>Staff recorded a refund as sent. The buyer is told how much and where it went.</summary>
/// <param name="RefundId">The refund.</param>
/// <param name="OrderId">The order it is for.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="Amount">How much.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="Method">Razorpay (back to the original payment) or Upi.</param>
/// <param name="UpiId">Where a UPI refund was sent.</param>
public sealed record RefundMadeDomainEvent(
    Guid RefundId,
    Guid OrderId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string Method,
    string? UpiId) : DomainEvent;
