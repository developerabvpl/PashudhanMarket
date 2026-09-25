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

    /// <summary>
    /// Creates the carrier's order for a buyer's return: collected from the buyer, delivered to the
    /// seller. The carrier schedules the collection itself once an AWB is assigned.
    /// </summary>
    Task<Result<CarrierOrder>> CreateReturnOrderAsync(CourierReturnRequest request, CancellationToken cancellationToken);

    /// <summary>Issues an AWB for a return order.</summary>
    Task<Result<CarrierAwb>> AssignReturnAwbAsync(string carrierShipmentId, CancellationToken cancellationToken);

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
    decimal ShippingCharges,
    int WeightGrams,
    decimal LengthCm,
    decimal BreadthCm,
    decimal HeightCm);

/// <summary>Everything a carrier needs to book a buyer's return.</summary>
/// <param name="Reference">Our reference on the carrier's side.</param>
/// <param name="OrderDateUtc">When the return was booked.</param>
/// <param name="CollectFrom">The buyer, at the address the goods were delivered to.</param>
/// <param name="DeliverTo">The seller, at their registered address.</param>
/// <param name="Items">What is coming back.</param>
/// <param name="SubTotal">Declared value.</param>
/// <param name="WeightGrams">Packed weight.</param>
/// <param name="LengthCm">Box length.</param>
/// <param name="BreadthCm">Box breadth.</param>
/// <param name="HeightCm">Box height.</param>
public sealed record CourierReturnRequest(
    string Reference,
    DateTime OrderDateUtc,
    CourierAddress CollectFrom,
    CourierAddress DeliverTo,
    IReadOnlyList<CourierOrderItem> Items,
    decimal SubTotal,
    int WeightGrams,
    decimal LengthCm,
    decimal BreadthCm,
    decimal HeightCm);

/// <summary>A person at an address, for either end of a return.</summary>
public sealed record CourierAddress(
    string Name,
    string Phone,
    string Line1,
    string? Line2,
    string City,
    string State,
    string Pincode);

/// <summary>One product line in a consignment.</summary>
public sealed record CourierOrderItem(string Name, string Sku, int Units, decimal SellingPrice);
