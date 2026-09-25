using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.Modules.Shipping.Gateway;

namespace UPBazaar.Modules.Shipping.Application;

/// <summary>Maps shipments to the DTO the API shows. Requires the events and charges to be loaded.</summary>
internal static class ShipmentMappings
{
    /// <param name="shipment">The shipment.</param>
    /// <param name="courier">For the tracking link.</param>
    /// <param name="withCharges">Include what the courier charges; false for buyers.</param>
    public static ShipmentDto ToDto(this Shipment shipment, ICourierGateway courier, bool withCharges = true) => new(
        shipment.PublicId,
        shipment.OrderId,
        shipment.OrderNumber,
        shipment.OrderPartId,
        shipment.SellerId,
        shipment.Status.ToString(),
        shipment.Direction.ToString(),
        shipment.Carrier,
        shipment.PickupLocation,
        new ParcelDto(shipment.WeightGrams, shipment.LengthCm, shipment.BreadthCm, shipment.HeightCm),
        shipment.PaymentMode,
        shipment.CodAmount,
        shipment.CarrierOrderId,
        shipment.Awb,
        shipment.CourierName,
        shipment.Awb is { } awb && courier.IsEnabled ? courier.TrackingUrl(awb) : null,
        shipment.LastError,
        [.. shipment.Events.OrderBy(e => e.Id).Select(e => new ShipmentEventDto(e.Status, e.OccurredAtUtc))],
        shipment.CreatedAtUtc,
        withCharges ? shipment.QuoteError : null,
        [.. shipment.Charges
            .Where(_ => withCharges)
            .OrderBy(c => c.Trip)
            .Select(c => new ShipmentChargeDto(c.Trip.ToString(), c.Amount, c.Billed, c.IncurredAtUtc, c.CorrectedAtUtc, c.Note))]);
}
