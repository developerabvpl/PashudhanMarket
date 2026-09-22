using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.Modules.Shipping.Gateway;

namespace UPBazaar.Modules.Shipping.Application;

/// <summary>Maps shipments to the DTO the API shows. Requires the events to be loaded.</summary>
internal static class ShipmentMappings
{
    public static ShipmentDto ToDto(this Shipment shipment, ICourierGateway courier) => new(
        shipment.PublicId,
        shipment.OrderId,
        shipment.OrderNumber,
        shipment.OrderPartId,
        shipment.SellerId,
        shipment.Status.ToString(),
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
        shipment.CreatedAtUtc);
}
