using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Domain;

public enum ShipmentStatus
{
    Pending = 0,
    Booked = 1,
    InTransit = 2,
    Delivered = 3,
    Cancelled = 4,
}

public sealed class Shipment : AggregateRoot, IAuditable
{
    private Shipment()
    {
    }

    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; } = null!;

    public string DeliveryPostcode { get; private set; } = null!;

    public string? AwbNumber { get; private set; }

    public string? Courier { get; private set; }

    public ShipmentStatus Status { get; private set; }

    public DateTime? ExpectedDeliveryUtc { get; private set; }

    public DateTime? DeliveredAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public static Shipment ForOrder(Guid orderId, string orderNumber, string deliveryPostcode) => new()
    {
        OrderId = orderId,
        OrderNumber = orderNumber,
        DeliveryPostcode = deliveryPostcode,
        Status = ShipmentStatus.Pending,
    };

    public Result Book(string awbNumber, string courier, DateTime? expectedDeliveryUtc)
    {
        if (Status != ShipmentStatus.Pending)
        {
            return Result.Failure(ShippingErrors.AlreadyBooked);
        }

        AwbNumber = awbNumber;
        Courier = courier;
        ExpectedDeliveryUtc = expectedDeliveryUtc;
        Status = ShipmentStatus.Booked;

        return Result.Success();
    }

    public void MarkInTransit() => Status = ShipmentStatus.InTransit;

    public void MarkDelivered(DateTime deliveredAtUtc)
    {
        Status = ShipmentStatus.Delivered;
        DeliveredAtUtc = deliveredAtUtc;
    }

    public Result Cancel()
    {
        if (Status == ShipmentStatus.Delivered)
        {
            return Result.Failure(ShippingErrors.NotCancellable);
        }

        Status = ShipmentStatus.Cancelled;

        return Result.Success();
    }
}
