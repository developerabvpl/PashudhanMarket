namespace UPBazaar.Modules.Shipping.Contracts.Dtos;

public sealed record ShipmentDto(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    string Status,
    string? AwbNumber,
    string? Courier,
    string DeliveryPostcode,
    DateTime? ExpectedDeliveryUtc,
    DateTime? DeliveredAtUtc);

public sealed record ShipmentTrackingDto(string AwbNumber, string Status, DateTime UpdatedAtUtc);
