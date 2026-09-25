namespace UPBazaar.Modules.Shipping.Contracts.Dtos;

/// <summary>A parcel as packed.</summary>
/// <param name="WeightGrams">Total weight in grams.</param>
/// <param name="LengthCm">Box length in centimetres.</param>
/// <param name="BreadthCm">Box breadth in centimetres.</param>
/// <param name="HeightCm">Box height in centimetres.</param>
public sealed record ParcelDto(int WeightGrams, decimal LengthCm, decimal BreadthCm, decimal HeightCm);

/// <summary>
/// The parcel worked out from the products' recorded packages, for the packing form to start from.
/// </summary>
/// <param name="Parcel">The worked-out parcel, or null when some product has no package recorded.</param>
/// <param name="MissingSkus">SKUs of the products whose package has not been recorded.</param>
/// <param name="PickupLocation">Where the courier will collect it from, or null when none is set up.</param>
public sealed record ParcelSuggestionDto(ParcelDto? Parcel, IReadOnlyList<string> MissingSkus, string? PickupLocation);

/// <summary>A courier shipment for one seller's part of an order.</summary>
/// <param name="Id">Public id.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">That order's number.</param>
/// <param name="OrderPartId">The seller's part being shipped.</param>
/// <param name="SellerId">Whose goods.</param>
/// <param name="Status">Booking, PickupRequested, InTransit, Delivered, ReturnInTransit, Returned or Cancelled.</param>
/// <param name="Direction">Forward (to the buyer) or Return (a buyer's return, collected from them and taken to the seller).</param>
/// <param name="Carrier">Shiprocket, or Fake in development.</param>
/// <param name="PickupLocation">The pickup location's name as registered with the carrier.</param>
/// <param name="Parcel">What was booked.</param>
/// <param name="PaymentMode">COD or Prepaid.</param>
/// <param name="CodAmount">What the courier collects at the door; zero when prepaid.</param>
/// <param name="CarrierOrderId">The carrier's order id, once booked.</param>
/// <param name="Awb">Air waybill number, the courier's tracking number, once assigned.</param>
/// <param name="CourierName">The courier the carrier assigned.</param>
/// <param name="TrackingUrl">A public tracking page for the buyer, once there is an AWB.</param>
/// <param name="LastError">Why the last booking step failed, if it did; packing again resumes from there.</param>
/// <param name="Events">What the courier has reported, oldest first.</param>
/// <param name="CreatedAtUtc">When it was booked.</param>
/// <param name="QuoteError">Why the courier charge could not be quoted; staff enter it from the invoice.</param>
/// <param name="Charges">What each courier trip costs the seller, or will.</param>
public sealed record ShipmentDto(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    Guid OrderPartId,
    Guid SellerId,
    string Status,
    string Direction,
    string Carrier,
    string PickupLocation,
    ParcelDto Parcel,
    string PaymentMode,
    decimal CodAmount,
    string? CarrierOrderId,
    string? Awb,
    string? CourierName,
    string? TrackingUrl,
    string? LastError,
    IReadOnlyList<ShipmentEventDto> Events,
    DateTime CreatedAtUtc,
    string? QuoteError,
    IReadOnlyList<ShipmentChargeDto> Charges);

/// <summary>One courier update.</summary>
/// <param name="Status">The courier's own wording, such as IN TRANSIT.</param>
/// <param name="OccurredAtUtc">When it was received.</param>
public sealed record ShipmentEventDto(string Status, DateTime OccurredAtUtc);

/// <summary>Where a courier collects parcels from.</summary>
/// <param name="SellerId">The seller it belongs to, or null for the platform warehouse every other seller ships from.</param>
/// <param name="Name">The pickup location's name exactly as registered in Shiprocket.</param>
/// <param name="Pincode">The PIN code of that address, used to price parcels; null if not recorded.</param>
/// <param name="UpdatedAtUtc">When it was last set.</param>
public sealed record PickupLocationDto(Guid? SellerId, string Name, string? Pincode, DateTime UpdatedAtUtc);

/// <summary>What one courier trip of a shipment costs.</summary>
/// <param name="Trip">Delivery, Rto (brought back undelivered) or ReturnPickup (collecting a buyer's return).</param>
/// <param name="Amount">The charge, as quoted or corrected; null while unknown.</param>
/// <param name="Charged">How much has been charged so far: nothing until the trip happens.</param>
/// <param name="IncurredAtUtc">When the trip happened; null until it does.</param>
/// <param name="CorrectedAtUtc">When staff last corrected the amount.</param>
/// <param name="Note">Why it was corrected.</param>
public sealed record ShipmentChargeDto(
    string Trip,
    decimal? Amount,
    decimal Charged,
    DateTime? IncurredAtUtc,
    DateTime? CorrectedAtUtc,
    string? Note);
