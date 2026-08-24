namespace UPBazaar.Infrastructure.ExternalServices.Shipping;

public sealed record ShipmentBookingRequest(
    string IdempotencyKey,
    string OrderReference,
    string PickupPostcode,
    string DeliveryPostcode,
    decimal WeightKg,
    decimal DeclaredValue);

public sealed record ShipmentBooking(string AwbNumber, string Courier, DateTime? ExpectedDeliveryUtc);

public sealed record ShipmentTracking(string AwbNumber, string Status, DateTime UpdatedAtUtc);

/// <summary>Logistics provider boundary. Booking must be idempotent on IdempotencyKey.</summary>
public interface IShippingProvider
{
    string Provider { get; }

    Task<ShipmentBooking> CreateShipmentAsync(
        ShipmentBookingRequest request,
        CancellationToken cancellationToken);

    Task<ShipmentTracking> TrackAsync(string awbNumber, CancellationToken cancellationToken);

    Task CancelAsync(string awbNumber, CancellationToken cancellationToken);
}
