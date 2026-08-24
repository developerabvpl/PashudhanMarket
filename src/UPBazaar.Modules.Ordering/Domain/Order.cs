using UPBazaar.Modules.Ordering.Contracts.Events;
using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Ordering.Domain;

public enum OrderStatus
{
    AwaitingPayment = 0,
    Paid = 1,
    Cancelled = 2,
    Fulfilled = 3,
}

public sealed class Order : AggregateRoot, IAuditable
{
    private readonly List<OrderLine> _lines = [];

    private Order()
    {
    }

    public string OrderNumber { get; private set; } = null!;

    public Guid CustomerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public string Currency { get; private set; } = "INR";

    public decimal Subtotal { get; private set; }

    public decimal ShippingFee { get; private set; }

    public decimal Total { get; private set; }

    public string DeliveryPostcode { get; private set; } = null!;

    public DateTime PlacedAtUtc { get; private set; }

    public Guid? PaymentId { get; private set; }

    public string? GatewayOrderId { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public static Result<Order> Place(
        Guid customerId,
        string orderNumber,
        string deliveryPostcode,
        string currency,
        decimal shippingFee,
        IReadOnlyCollection<(Guid ProductId, string Sku, string Name, decimal UnitPrice, int Quantity)> lines,
        DateTime placedAtUtc)
    {
        if (lines.Count == 0)
        {
            return Result.Failure<Order>(OrderErrors.EmptyCart);
        }

        var order = new Order
        {
            CustomerId = customerId,
            OrderNumber = orderNumber,
            DeliveryPostcode = deliveryPostcode.Trim(),
            Currency = currency.ToUpperInvariant(),
            Status = OrderStatus.AwaitingPayment,
            ShippingFee = shippingFee,
            PlacedAtUtc = placedAtUtc,
        };

        foreach (var line in lines)
        {
            order._lines.Add(OrderLine.Create(
                line.ProductId,
                line.Sku,
                line.Name,
                line.UnitPrice,
                line.Quantity));
        }

        order.Subtotal = order._lines.Sum(l => l.LineTotal);
        order.Total = order.Subtotal + order.ShippingFee;

        order.Raise(new OrderPlacedDomainEvent(
            order.PublicId,
            order.OrderNumber,
            order.CustomerId,
            order.Total,
            order.Currency,
            order.DeliveryPostcode,
            order._lines.Select(l => new OrderLineSnapshot(l.ProductId, l.Sku, l.Quantity)).ToList()));

        return order;
    }

    /// <summary>Records the payment opened for this order so the client can complete checkout.</summary>
    public void AttachPayment(Guid paymentId, string gatewayOrderId)
    {
        PaymentId = paymentId;
        GatewayOrderId = gatewayOrderId;
    }

    public Result MarkPaid(Guid paymentId)
    {
        if (Status == OrderStatus.Paid)
        {
            return Result.Success();
        }

        if (Status is OrderStatus.Cancelled)
        {
            return Result.Failure(OrderErrors.NotCancellable);
        }

        Status = OrderStatus.Paid;
        PaymentId = paymentId;
        Raise(new OrderPaidDomainEvent(PublicId, OrderNumber, paymentId));

        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (Status is OrderStatus.Paid or OrderStatus.Fulfilled)
        {
            return Result.Failure(OrderErrors.NotCancellable);
        }

        if (Status == OrderStatus.Cancelled)
        {
            return Result.Success();
        }

        Status = OrderStatus.Cancelled;

        Raise(new OrderCancelledDomainEvent(
            PublicId,
            OrderNumber,
            reason,
            _lines.Select(l => new OrderLineSnapshot(l.ProductId, l.Sku, l.Quantity)).ToList()));

        return Result.Success();
    }

    /// <summary>Human-facing, sortable, and unique enough for a day of orders.</summary>
    public static string NextOrderNumber(DateTime utcNow) =>
        $"UPB-{utcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}
