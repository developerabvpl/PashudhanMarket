namespace UPBazaar.Modules.Payments.Contracts.Dtos;

public sealed record PaymentDto(
    Guid Id,
    Guid OrderId,
    string GatewayOrderId,
    string Provider,
    decimal Amount,
    string Currency,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? CapturedAtUtc);

/// <summary>What the storefront needs to hand to the gateway checkout widget.</summary>
public sealed record PaymentInitiationDto(
    Guid PaymentId,
    string GatewayOrderId,
    string Provider,
    decimal Amount,
    string Currency);

public sealed record RefundDto(
    Guid Id,
    Guid PaymentId,
    string GatewayRefundId,
    decimal Amount,
    string Status,
    DateTime CreatedAtUtc);

public sealed record SettlementLineDto(
    Guid Id,
    Guid PaymentId,
    decimal GrossAmount,
    decimal CommissionAmount,
    decimal NetAmount,
    string Status,
    DateTime? SettledAtUtc);

/// <summary>Request from Ordering to open a payment for a freshly placed order.</summary>
public sealed record CreatePaymentForOrderRequest(
    Guid OrderId,
    string OrderNumber,
    Guid SellerId,
    decimal Amount,
    string Currency,
    string IdempotencyKey);
