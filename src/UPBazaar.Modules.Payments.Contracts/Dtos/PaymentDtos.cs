namespace UPBazaar.Modules.Payments.Contracts.Dtos;

/// <summary>Whether the storefront may offer online payment, and through what.</summary>
/// <param name="OnlineEnabled">False when no payment gateway is configured; checkout then offers cash on delivery only.</param>
/// <param name="Gateway">Razorpay, or Fake in development where a simulated payment stands in for Razorpay.</param>
public sealed record PaymentsConfigDto(bool OnlineEnabled, string Gateway);

/// <summary>Everything the browser needs to open Razorpay Checkout for one order.</summary>
/// <param name="PaymentId">Our id for this payment attempt.</param>
/// <param name="Gateway">Razorpay or Fake. With Fake there is no Razorpay window: the storefront simulates the payment instead.</param>
/// <param name="KeyId">Razorpay's public key id, passed to Checkout as <c>key</c>. Never the secret.</param>
/// <param name="GatewayOrderId">Razorpay's order id, passed to Checkout as <c>order_id</c>.</param>
/// <param name="AmountInPaise">Amount in the smallest currency unit, as Checkout expects it.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="OrderNumber">Shown in the Checkout window's description.</param>
/// <param name="ExpiresAtUtc">When the order is cancelled if unpaid. The storefront sets Checkout's timeout from this.</param>
public sealed record CheckoutSessionDto(
    Guid PaymentId,
    string Gateway,
    string KeyId,
    string GatewayOrderId,
    long AmountInPaise,
    string Currency,
    string OrderNumber,
    DateTime ExpiresAtUtc);

/// <summary>What happened after a payment was reported.</summary>
/// <param name="OrderId">The order it paid for.</param>
/// <param name="Outcome">
/// Confirmed (the order is paid and confirmed), RefundDue (the money was taken but the order could
/// not accept it - usually because it had already been cancelled - so it will be refunded), or
/// Processing (paid, and the order will be confirmed shortly).
/// </param>
public sealed record PaymentResultDto(Guid OrderId, string Outcome);

/// <summary>A payment as staff see it.</summary>
/// <param name="Id">Public id.</param>
/// <param name="OrderId">The order it is for.</param>
/// <param name="OrderNumber">That order's number.</param>
/// <param name="BuyerId">Who is paying.</param>
/// <param name="Amount">Amount due or taken.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="Status">Created (awaiting the buyer) or Paid.</param>
/// <param name="OrderOutcome">
/// Pending, Confirmed, Refused (money taken for an order that could not accept it), or Cancelled
/// (the order took it and was cancelled later; its refunds are recorded part by part).
/// </param>
/// <param name="Gateway">Razorpay or Fake.</param>
/// <param name="GatewayOrderId">Razorpay's order id.</param>
/// <param name="GatewayPaymentId">Razorpay's payment id, once paid. What to search the dashboard for.</param>
/// <param name="LastFailure">Why the latest attempt failed, if one did.</param>
/// <param name="CreatedAtUtc">When the buyer started paying.</param>
/// <param name="PaidAtUtc">When the money was taken.</param>
/// <param name="RefundDue">Sum of refunds recorded but not yet made.</param>
public sealed record PaymentDto(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    Guid BuyerId,
    decimal Amount,
    string Currency,
    string Status,
    string OrderOutcome,
    string Gateway,
    string GatewayOrderId,
    string? GatewayPaymentId,
    string? LastFailure,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    decimal RefundDue);

/// <summary>Money owed back to a buyer, and whether it has gone back yet.</summary>
/// <param name="Id">Public id.</param>
/// <param name="PaymentId">The online payment it comes out of; null for a UPI refund of cash paid at the door.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">That order's number.</param>
/// <param name="OrderPartId">The seller's part it is for; null when the whole payment is refunded.</param>
/// <param name="Amount">How much.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="Reason">Why it is owed.</param>
/// <param name="Status">Due, or Refunded once someone has made it and recorded it.</param>
/// <param name="Method">Razorpay (reverse the online payment) or Upi (send it to <paramref name="UpiId"/>).</param>
/// <param name="UpiId">Where a UPI refund is sent; null for Razorpay.</param>
/// <param name="GatewayPaymentId">The Razorpay payment to refund against; null for UPI.</param>
/// <param name="GatewayRefundId">Razorpay's refund id, or the UPI transaction reference (UTR), once refunded.</param>
/// <param name="CreatedAtUtc">When it became owed.</param>
/// <param name="RefundedAtUtc">When it was recorded as made.</param>
/// <param name="RefundedBy">Who recorded it.</param>
public sealed record RefundDto(
    Guid Id,
    Guid? PaymentId,
    Guid OrderId,
    string OrderNumber,
    Guid? OrderPartId,
    decimal Amount,
    string Currency,
    string Reason,
    string Status,
    string Method,
    string? UpiId,
    string? GatewayPaymentId,
    string? GatewayRefundId,
    DateTime CreatedAtUtc,
    DateTime? RefundedAtUtc,
    string? RefundedBy);
