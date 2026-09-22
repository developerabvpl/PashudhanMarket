using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Gateway;

/// <summary>
/// The shipping aggregator, as far as this module needs it: book a consignment in three steps,
/// cancel one, and recognise its tracking webhooks.
///
/// Three implementations, as with payments: <see cref="ShiprocketGateway"/> for real,
/// <see cref="FakeCourierGateway"/> for development and tests, and <see cref="UnconfiguredCourierGateway"/>
/// for a server without credentials, which turns courier booking off rather than half-working.
/// </summary>
public interface ICourierGateway
{
    /// <summary>Shiprocket, Fake, or None.</summary>
    string Name { get; }

    bool IsEnabled { get; }

    /// <summary>Creates the carrier's order for one consignment.</summary>
    Task<Result<CarrierOrder>> CreateOrderAsync(CourierOrderRequest request, CancellationToken cancellationToken);

    /// <summary>Asks the carrier to pick a courier and issue an AWB.</summary>
    Task<Result<CarrierAwb>> AssignAwbAsync(string carrierShipmentId, CancellationToken cancellationToken);

    /// <summary>Asks the courier to come and collect.</summary>
    Task<Result> RequestPickupAsync(string carrierShipmentId, CancellationToken cancellationToken);

    /// <summary>Cancels a consignment the courier has not collected yet.</summary>
    Task<Result> CancelAsync(string carrierOrderId, CancellationToken cancellationToken);

    /// <summary>Checks the shared token a tracking webhook carries.</summary>
    bool IsWebhookTokenValid(string? token);

    /// <summary>A public page where the buyer can follow the parcel.</summary>
    string TrackingUrl(string awb);
}

/// <summary>The carrier's ids for a booked consignment.</summary>
public sealed record CarrierOrder(string CarrierOrderId, string CarrierShipmentId);

/// <summary>The AWB and the courier behind it.</summary>
public sealed record CarrierAwb(string Awb, string CourierName);

/// <summary>Everything a carrier needs to book one consignment.</summary>
public sealed record CourierOrderRequest(
    string Reference,
    DateTime OrderDateUtc,
    string PickupLocation,
    string CustomerName,
    string CustomerPhone,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string Pincode,
    IReadOnlyList<CourierOrderItem> Items,
    bool CashOnDelivery,
    decimal SubTotal,
    int WeightGrams,
    decimal LengthCm,
    decimal BreadthCm,
    decimal HeightCm);

/// <summary>One product line in a consignment.</summary>
public sealed record CourierOrderItem(string Name, string Sku, int Units, decimal SellingPrice);
