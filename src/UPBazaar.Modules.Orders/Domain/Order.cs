using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Domain;

/// <summary>
/// One checkout: what was bought, from whom, at what price, delivered where, paid how.
///
/// Everything a buyer agreed to is copied in at placement - names, SKUs, prices, the address - so
/// an order reads the same years later however the catalogue has changed. It is split into a
/// <see cref="OrderPart"/> per seller, because sellers pack and ship independently: one part can
/// be delivered while another is still being packed, or has been cancelled.
///
/// Stock is not this class's business. Checkout reserves it before an order exists, and the
/// handlers commit, release or return it alongside each state change here.
/// </summary>
public sealed class Order : AggregateRoot, IAuditable
{
    /// <summary>How long an online order waits for its payment before it is cancelled.</summary>
    public static readonly TimeSpan PaymentWindow = TimeSpan.FromMinutes(15);

    private readonly List<OrderPart> _parts = [];

    private Order()
    {
    }

    public string Number { get; private set; } = string.Empty;

    /// <summary>Identity's public id for the buyer.</summary>
    public Guid BuyerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public PaymentMethod PaymentMethod { get; private set; }

    public PaymentStatus PaymentStatus { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public decimal ShippingFee { get; private set; }

    public DeliveryAddress DeliveryAddress { get; private set; } = null!;

    /// <summary>Inventory's reservation for this order's stock: released on cancel, committed on confirmation.</summary>
    public Guid ReservationId { get; private set; }

    public DateTime PlacedAtUtc { get; private set; }

    public DateTime? PaymentDueAtUtc { get; private set; }

    /// <summary>The payment provider's id for the payment, once paid online.</summary>
    public string? PaymentReference { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    public IReadOnlyCollection<OrderPart> Parts => _parts.AsReadOnly();

    /// <summary>Optimistic concurrency token: a payment and a cancel landing together must not both win.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>What is still coming: cancelled parts drop out, so a partial refund shows in the total.</summary>
    public decimal Subtotal => _parts.Where(p => p.Status != OrderPartStatus.Cancelled).Sum(p => p.Subtotal);

    public decimal Total => Subtotal + ShippingFee;

    /// <summary>
    /// A buyer may cancel until something has shipped. After that the goods are on a truck and
    /// it is a return, which is Shipping's to handle.
    /// </summary>
    public bool CanCancel =>
        Status is OrderStatus.PendingPayment or OrderStatus.Confirmed
        && _parts.All(p => p.Status is not (OrderPartStatus.Shipped or OrderPartStatus.Delivered));

    /// <summary>
    /// Creates an order from checked-out lines. A cash-on-delivery order is confirmed on the spot,
    /// so the caller must already have committed its stock; an online one waits for payment
    /// with its stock held.
    /// </summary>
    public static Order Place(
        string number,
        Guid buyerId,
        PaymentMethod paymentMethod,
        DeliveryAddress deliveryAddress,
        string currency,
        IReadOnlyList<OrderLineInput> lines,
        Guid reservationId,
        DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentNullException.ThrowIfNull(deliveryAddress);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw new ArgumentException("An order needs at least one line.", nameof(lines));
        }

        var isCod = paymentMethod == PaymentMethod.CashOnDelivery;

        var order = new Order
        {
            Number = number,
            BuyerId = buyerId,
            PaymentMethod = paymentMethod,
            PaymentStatus = isCod ? PaymentStatus.CashOnDelivery : PaymentStatus.Pending,
            Status = isCod ? OrderStatus.Confirmed : OrderStatus.PendingPayment,
            Currency = currency,
            DeliveryAddress = deliveryAddress,
            ReservationId = reservationId,
            PlacedAtUtc = now,
            PaymentDueAtUtc = isCod ? null : now + PaymentWindow,
        };

        // Sellers in the order their first product appears in the cart, so the order reads the
        // way the cart did.
        foreach (var seller in lines.GroupBy(l => l.SellerId))
        {
            order._parts.Add(OrderPart.Create(
                seller.Key,
                isCod ? OrderPartStatus.Confirmed : OrderPartStatus.AwaitingPayment,
                seller));
        }

        order.Raise(new OrderPlacedDomainEvent(
            order.PublicId, number, buyerId, paymentMethod.ToString(), order.Total, currency));

        if (isCod)
        {
            order.RaiseConfirmed();
        }

        return order;
    }

    /// <summary>
    /// Records the online payment. The caller commits the reservation in the same transaction.
    /// Repeating it with the same reference is a no-op, so a redelivered webhook does no harm.
    /// </summary>
    public Result ConfirmPayment(decimal amount, string paymentReference, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentReference);

        if (PaymentStatus == PaymentStatus.Paid)
        {
            return PaymentReference == paymentReference
                ? Result.Success()
                : Result.Failure(OrderErrors.AlreadyPaidElsewhere);
        }

        if (Status != OrderStatus.PendingPayment)
        {
            return Result.Failure(OrderErrors.NotAwaitingPayment);
        }

        if (now > PaymentDueAtUtc)
        {
            return Result.Failure(OrderErrors.PaymentTooLate);
        }

        if (amount != Total)
        {
            return Result.Failure(OrderErrors.AmountMismatch);
        }

        PaymentStatus = PaymentStatus.Paid;
        PaymentReference = paymentReference;
        PaymentDueAtUtc = null;
        Status = OrderStatus.Confirmed;

        foreach (var part in _parts)
        {
            part.MoveTo(OrderPartStatus.Confirmed);
        }

        RaiseConfirmed();

        return Result.Success();
    }

    /// <summary>
    /// Cancels everything that is still coming. Returns the parts that were cancelled by this call,
    /// so the caller knows whose stock to put back.
    /// </summary>
    public Result<IReadOnlyList<OrderPart>> Cancel(string reason, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!CanCancel)
        {
            return Result.Failure<IReadOnlyList<OrderPart>>(OrderErrors.CannotCancel);
        }

        var cancelled = _parts.Where(p => p.Status != OrderPartStatus.Cancelled).ToList();

        foreach (var part in cancelled)
        {
            CancelPartInternal(part, reason);
        }

        MarkCancelled(reason, now);

        return cancelled;
    }

    /// <summary>
    /// Cancels one seller's part - the seller cannot supply it, say. The rest of the order carries
    /// on; cancelling the last live part cancels the order.
    /// </summary>
    public Result CancelPart(Guid partId, string reason, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var part = _parts.FirstOrDefault(p => p.PublicId == partId);

        if (part is null)
        {
            return Result.Failure(OrderErrors.PartNotFound);
        }

        // Before payment the stock is one reservation for the whole order, which cannot be
        // released in pieces; and there is nothing to refund, so cancelling it all is no loss.
        if (part.Status == OrderPartStatus.AwaitingPayment)
        {
            return Result.Failure(OrderErrors.PartAwaitingPayment);
        }

        if (part.Status is not (OrderPartStatus.Confirmed or OrderPartStatus.Packed))
        {
            return Result.Failure(OrderErrors.PartCannotCancel);
        }

        CancelPartInternal(part, reason);

        if (_parts.All(p => p.Status == OrderPartStatus.Cancelled))
        {
            MarkCancelled(reason, now);
        }
        else
        {
            CompleteIfDone();
        }

        return Result.Success();
    }

    /// <summary>
    /// Moves a part forward: Confirmed, Packed, Shipped, Delivered. A step may be skipped - a
    /// courier scan can report Shipped for a part nobody marked Packed - but never taken back.
    /// </summary>
    public Result AdvancePart(Guid partId, OrderPartStatus target)
    {
        var part = _parts.FirstOrDefault(p => p.PublicId == partId);

        if (part is null)
        {
            return Result.Failure(OrderErrors.PartNotFound);
        }

        if (target is not (OrderPartStatus.Packed or OrderPartStatus.Shipped or OrderPartStatus.Delivered)
            || part.Status is OrderPartStatus.AwaitingPayment or OrderPartStatus.Cancelled
            || target <= part.Status)
        {
            return Result.Failure(OrderErrors.InvalidTransition);
        }

        part.MoveTo(target);
        CompleteIfDone();

        return Result.Success();
    }

    private void CancelPartInternal(OrderPart part, string reason)
    {
        var refund = PaymentStatus == PaymentStatus.Paid ? part.Subtotal : 0m;

        part.Cancel(reason);

        Raise(new OrderPartCancelledDomainEvent(
            PublicId, Number, part.PublicId, part.SellerId, refund, Currency));
    }

    private void MarkCancelled(string reason, DateTime now)
    {
        Status = OrderStatus.Cancelled;
        CancelledAtUtc = now;
        CancellationReason = reason;
        PaymentDueAtUtc = null;

        Raise(new OrderCancelledDomainEvent(PublicId, Number, reason));
    }

    private void CompleteIfDone()
    {
        var live = _parts.Where(p => p.Status != OrderPartStatus.Cancelled).ToList();

        if (live.Count > 0 && live.All(p => p.Status == OrderPartStatus.Delivered))
        {
            Status = OrderStatus.Completed;
        }
    }

    private void RaiseConfirmed() =>
        Raise(new OrderConfirmedDomainEvent(PublicId, Number, [.. _parts.Select(p => p.PublicId)]));
}

/// <summary>A checked-out line on its way into an order.</summary>
public sealed record OrderLineInput(
    Guid SellerId,
    Guid ProductId,
    string Sku,
    string Name,
    decimal UnitPrice,
    int Quantity);

/// <summary>One seller's share of an order, fulfilled and cancelled on its own.</summary>
public sealed class OrderPart : Entity
{
    private readonly List<OrderLine> _lines = [];

    private OrderPart()
    {
    }

    public long OrderId { get; private set; }

    public Guid SellerId { get; private set; }

    public OrderPartStatus Status { get; private set; }

    public string? CancellationReason { get; private set; }

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    public decimal Subtotal => _lines.Sum(l => l.LineTotal);

    internal static OrderPart Create(Guid sellerId, OrderPartStatus status, IEnumerable<OrderLineInput> lines)
    {
        var part = new OrderPart { SellerId = sellerId, Status = status };

        part._lines.AddRange(lines.Select(OrderLine.Create));

        return part;
    }

    internal void MoveTo(OrderPartStatus status) => Status = status;

    internal void Cancel(string reason)
    {
        Status = OrderPartStatus.Cancelled;
        CancellationReason = reason;
    }
}

/// <summary>One product in an order, frozen at the price and name it was bought under.</summary>
public sealed class OrderLine : Entity
{
    private OrderLine()
    {
    }

    public long OrderPartId { get; private set; }

    public Guid ProductId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;

    internal static OrderLine Create(OrderLineInput input) => new()
    {
        ProductId = input.ProductId,
        Sku = input.Sku,
        Name = input.Name,
        UnitPrice = input.UnitPrice,
        Quantity = input.Quantity,
    };
}
