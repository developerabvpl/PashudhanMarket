namespace UPBazaar.Modules.Ordering.Contracts.Dtos;

public sealed record OrderLineDto(
    Guid ProductId,
    string Sku,
    string Name,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string Status,
    decimal Subtotal,
    decimal ShippingFee,
    decimal Total,
    string Currency,
    DateTime PlacedAtUtc,
    IReadOnlyList<OrderLineDto> Lines,
    Guid? PaymentId,
    string? GatewayOrderId);

public sealed record OrderSummaryDto(
    Guid Id,
    string OrderNumber,
    string Status,
    decimal Total,
    string Currency,
    DateTime PlacedAtUtc);
