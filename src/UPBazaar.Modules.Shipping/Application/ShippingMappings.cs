using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Domain;

namespace UPBazaar.Modules.Shipping.Application;

internal static class ShippingMappings
{
    public static ShipmentDto ToDto(this Shipment shipment) => new(
        shipment.PublicId,
        shipment.OrderId,
        shipment.OrderNumber,
        shipment.Status.ToString(),
        shipment.AwbNumber,
        shipment.Courier,
        shipment.DeliveryPostcode,
        shipment.ExpectedDeliveryUtc,
        shipment.DeliveredAtUtc);
}
