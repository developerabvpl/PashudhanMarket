using UPBazaar.Modules.Ordering.Contracts.Dtos;
using UPBazaar.Modules.Ordering.Domain;

namespace UPBazaar.Modules.Ordering.Application;

internal static class OrderingMappings
{
    public static OrderDto ToDto(this Order order) => new(
        order.PublicId,
        order.OrderNumber,
        order.CustomerId,
        order.Status.ToString(),
        order.Subtotal,
        order.ShippingFee,
        order.Total,
        order.Currency,
        order.PlacedAtUtc,
        order.Lines
            .Select(l => new OrderLineDto(l.ProductId, l.Sku, l.Name, l.UnitPrice, l.Quantity, l.LineTotal))
            .ToList(),
        order.PaymentId,
        order.GatewayOrderId);

    public static OrderSummaryDto ToSummary(this Order order) => new(
        order.PublicId,
        order.OrderNumber,
        order.Status.ToString(),
        order.Total,
        order.Currency,
        order.PlacedAtUtc);
}
