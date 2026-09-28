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

    /// <summary>
    /// The delivery charge the buyer pays: the sum of the parts' shares. It falls only when a
    /// cancelled part's share has nowhere to go and is given back.
    /// </summary>
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

    /// <summary>The coupon the order used, as stored; null when none.</summary>
    public string? CouponCode { get; private set; }

    /// <summary>
    /// Who bears the coupon's discount: Platform, or Seller. Decides whether sellers are paid on
    /// the full price or the discounted one.
    /// </summary>
    public string? CouponFundedBy { get; private set; }

    /// <summary>
    /// The least the goods the coupon covered had to come to, as it stood at checkout. If a return
    /// leaves the buyer keeping less than this, the discount on what they keep comes out of their
    /// refund. Null with no coupon, or one with no minimum.
    /// </summary>
    public decimal? CouponMinOrder { get; private set; }

    public IReadOnlyCollection<OrderPart> Parts => _parts.AsReadOnly();

    /// <summary>Optimistic concurrency token: a payment and a cancel landing together must not both win.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>
    /// The goods the buyer keeps: cancelled parts, RTOs and returned units drop out, so a refund
    /// shows in the total.
    /// </summary>
    public decimal Subtotal => _parts.Where(p => p.IsKept).Sum(p => p.KeptSubtotal);

    /// <summary>The coupon discount on the goods the buyer keeps.</summary>
    public decimal Discount => _parts.Where(p => p.IsKept).Sum(p => p.KeptDiscount);

    /// <summary>
    /// The delivery charge a free-delivery coupon lifts off the buyer: the shares of the parts it
    /// covers. The sellers still carry those shares - the platform or they themselves pay them.
    /// </summary>
    public decimal DeliveryDiscount => _parts.Where(p => p.FreeDelivery).Sum(p => p.DeliveryFee);

    public decimal Total => Subtotal - Discount + ShippingFee - DeliveryDiscount;

    /// <summary>
    /// A buyer may cancel until something has shipped. After that the goods are on a truck and
    /// it is a return, which is Shipping's to handle.
    /// </summary>
    public bool CanCancel =>
        Status is OrderStatus.PendingPayment or OrderStatus.Confirmed
        && _parts.All(p => p.Status is OrderPartStatus.AwaitingPayment or OrderPartStatus.Confirmed
            or OrderPartStatus.Packed or OrderPartStatus.Cancelled);

    /// <summary>
    /// Creates an order from checked-out lines. A cash-on-delivery order is confirmed on the spot,
    /// so the caller must already have committed its stock; an online one waits for payment
    /// with its stock held.
    ///
    /// A free-delivery coupon names the sellers whose parcels it covers in <paramref name="freeDeliveryFor"/>.
    /// </summary>
    public static Order Place(
        string number,
        Guid buyerId,
        PaymentMethod paymentMethod,
        DeliveryAddress deliveryAddress,
        string currency,
        IReadOnlyList<OrderLineInput> lines,
        decimal deliveryFee,
        (string Code, string FundedBy)? coupon,
        Guid reservationId,
        DateTime now,
        IReadOnlyCollection<Guid>? freeDeliveryFor = null,
        decimal? couponMinOrder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentOutOfRangeException.ThrowIfNegative(deliveryFee);
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
            CouponCode = coupon?.Code,
            CouponFundedBy = coupon?.FundedBy,
            CouponMinOrder = coupon is null ? null : couponMinOrder,
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

        order.ShareDeliveryFee(deliveryFee, order._parts);

        foreach (var part in order._parts.Where(p => freeDeliveryFor?.Contains(p.SellerId) == true))
        {
            part.MakeDeliveryFree();
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
    /// Delivery starts the buyer's return window, fixed now at <paramref name="returnWindow"/> so a
    /// later change of policy does not move the deadline on parcels already delivered.
    /// </summary>
    public Result AdvancePart(Guid partId, OrderPartStatus target, DateTime now, TimeSpan returnWindow)
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

        if (target == OrderPartStatus.Delivered)
        {
            part.RecordDelivered(now, returnWindow);

            Raise(new OrderPartDeliveredDomainEvent(
                PublicId,
                Number,
                part.PublicId,
                part.SellerId,
                part.Subtotal,
                Currency,
                now,
                now + returnWindow,
                SellerEarnsDelivery(part),
                SellerDiscount: CouponFundedBy == "Seller" ? part.KeptDiscount : 0m,
                CashOnDelivery: PaymentMethod == PaymentMethod.CashOnDelivery));
        }

        CompleteIfDone();

        return Result.Success();
    }

    /// <summary>
    /// The buyer asks to send a delivered part back. Allowed once per part, until its return window
    /// closes. A cash-on-delivery buyer must say where the refund goes, since there is no online
    /// payment to reverse; an online buyer's refund goes back the way it came, so any UPI id they
    /// sent is ignored.
    ///
    /// <paramref name="items"/> says how many units of which products go back; null or empty sends
    /// the whole parcel. One request per part: whatever it leaves out stays with the buyer.
    /// </summary>
    public Result RequestReturn(
        Guid partId,
        ReturnReason reason,
        string? comment,
        string? refundUpiId,
        DateTime now,
        IReadOnlyDictionary<Guid, int>? items = null)
    {
        var part = _parts.FirstOrDefault(p => p.PublicId == partId);

        if (part is null)
        {
            return Result.Failure(OrderErrors.PartNotFound);
        }

        if (part.ReturnRequest is not null)
        {
            return Result.Failure(OrderErrors.ReturnAlreadyRequested);
        }

        if (part.Status != OrderPartStatus.Delivered)
        {
            return Result.Failure(OrderErrors.NotDelivered);
        }

        if (part.ReturnWindowClosesAtUtc is not { } closes || now > closes)
        {
            return Result.Failure(OrderErrors.ReturnWindowClosed);
        }

        var isCod = PaymentMethod == PaymentMethod.CashOnDelivery;

        if (isCod && string.IsNullOrWhiteSpace(refundUpiId))
        {
            return Result.Failure(OrderErrors.RefundUpiIdRequired);
        }

        if (items is { Count: > 0 } && !items.All(i => part.Lines.Any(l => l.ProductId == i.Key && i.Value >= 1 && i.Value <= l.Quantity)))
        {
            return Result.Failure(OrderErrors.InvalidReturnItems);
        }

        foreach (var line in part.Lines)
        {
            line.AskToReturn(items is { Count: > 0 } ? items.GetValueOrDefault(line.ProductId) : line.Quantity);
        }

        part.RequestReturn(ReturnRequest.Create(reason, comment, isCod ? refundUpiId : null, now));

        Raise(new OrderPartReturnRequestedDomainEvent(PublicId, Number, part.PublicId, part.SellerId, reason.ToString()));

        return Result.Success();
    }

    /// <summary>
    /// The seller, or staff, accept a return: the part starts back, and Shipping books a pickup
    /// from the buyer when it hears of it.
    /// </summary>
    public Result ApproveReturn(Guid partId, string? note, string? decidedBy, DateTime now)
    {
        var part = _parts.FirstOrDefault(p => p.PublicId == partId);

        if (part is null)
        {
            return Result.Failure(OrderErrors.PartNotFound);
        }

        if (part.ReturnRequest?.Status != ReturnRequestStatus.Requested)
        {
            return Result.Failure(OrderErrors.ReturnNotPending);
        }

        part.ReturnRequest.Decide(approve: true, note, decidedBy, now);
        part.MoveTo(OrderPartStatus.Returning);

        foreach (var line in part.Lines)
        {
            line.Return();
        }

        var revoked = RevokeCouponIfBelowMinimum();
        part.ReturnRequest.SetRefund(Math.Max(0m, part.Lines.Sum(l => l.ReturnedPaid) - revoked.Values.Sum()));

        var sellerBears = CouponFundedBy == "Seller";

        Raise(new OrderPartReturnApprovedDomainEvent(
            PublicId,
            Number,
            part.PublicId,
            part.SellerId,
            KeptGross: part.KeptSubtotal - (sellerBears ? part.KeptDiscount : 0m)));

        // A seller who bore the coupon on a parcel already delivered to the buyer is owed the
        // discount taken back on it. One still on its way is paid on the reduced discount anyway.
        if (sellerBears)
        {
            foreach (var (other, amount) in revoked.Where(r => r.Key != part && r.Key.DeliveredAtUtc is not null))
            {
                Raise(new OrderPartDiscountRevokedDomainEvent(PublicId, Number, other.PublicId, other.SellerId, amount, Currency));
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// When a return leaves the buyer keeping less of what the coupon covered than its minimum
    /// order, they no longer qualified for it: the discount on everything they keep is taken back,
    /// out of this refund. Once only - revoked lines have no discount left to take.
    /// </summary>
    /// <returns>What was taken back, by part.</returns>
    private Dictionary<OrderPart, decimal> RevokeCouponIfBelowMinimum()
    {
        var kept = _parts.Where(p => p.IsKept).ToList();
        var covered = kept.SelectMany(p => p.Lines).Where(l => l.Discount > 0).ToList();

        if (CouponMinOrder is not { } minimum || covered.Sum(l => l.UnitPrice * l.KeptQuantity) >= minimum)
        {
            return [];
        }

        return kept
            .Select(p => (Part: p, Amount: p.Lines.Where(l => l.Discount > 0).Sum(l => l.RevokeDiscount())))
            .Where(x => x.Amount > 0)
            .ToDictionary(x => x.Part, x => x.Amount);
    }

    /// <summary>The seller, or staff, refuse a return, saying why. The part stays delivered.</summary>
    public Result RejectReturn(Guid partId, string note, string? decidedBy, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        var part = _parts.FirstOrDefault(p => p.PublicId == partId);

        if (part is null)
        {
            return Result.Failure(OrderErrors.PartNotFound);
        }

        if (part.ReturnRequest?.Status != ReturnRequestStatus.Requested)
        {
            return Result.Failure(OrderErrors.ReturnNotPending);
        }

        part.ReturnRequest.Decide(approve: false, note, decidedBy, now);

        Raise(new OrderPartReturnRejectedDomainEvent(PublicId, Number, part.PublicId, part.SellerId));

        return Result.Success();
    }

    /// <summary>
    /// The courier could not deliver and is taking the parcel back (RTO). Repeating it is a no-op.
    /// </summary>
    public Result StartReturn(Guid partId)
    {
        var part = _parts.FirstOrDefault(p => p.PublicId == partId);

        if (part is null)
        {
            return Result.Failure(OrderErrors.PartNotFound);
        }

        if (part.Status is OrderPartStatus.Returning or OrderPartStatus.Returned)
        {
            return Result.Success();
        }

        if (part.Status is not (OrderPartStatus.Packed or OrderPartStatus.Shipped))
        {
            return Result.Failure(OrderErrors.NotInTransit);
        }

        part.MoveTo(OrderPartStatus.Returning);

        return Result.Success();
    }

    /// <summary>
    /// The parcel is back with the seller. Its share is now owed back - only now, once the goods
    /// are in hand, because a return can still go wrong on the way.
    ///
    /// After an RTO that is only for an order paid online: cash on delivery collected nothing. After
    /// a buyer's return it is owed either way, since the buyer paid at the door, and a cash refund
    /// goes to the UPI id they gave. An order ends as cancelled only when nothing in it ever reached
    /// the buyer; an order whose goods were delivered and then sent back stays completed.
    /// </summary>
    public Result CompleteReturn(Guid partId, DateTime now)
    {
        var part = _parts.FirstOrDefault(p => p.PublicId == partId);

        if (part is null)
        {
            return Result.Failure(OrderErrors.PartNotFound);
        }

        if (part.Status == OrderPartStatus.Returned)
        {
            return Result.Success();
        }

        if (part.Status is not (OrderPartStatus.Packed or OrderPartStatus.Shipped or OrderPartStatus.Returning))
        {
            return Result.Failure(OrderErrors.NotInTransit);
        }

        var byBuyer = part.IsBuyerReturn;

        part.MoveTo(OrderPartStatus.Returned);

        Raise(new OrderPartReturnedDomainEvent(
            PublicId,
            Number,
            part.PublicId,
            part.SellerId,
            byBuyer ? part.ReturnRequest!.RefundDue ?? part.GoodsPaid
                : PaymentStatus == PaymentStatus.Paid ? part.GoodsPaid
                : 0m,
            Currency,
            RequestedByBuyer: byBuyer,
            RefundUpiId: part.ReturnRequest?.RefundUpiId));

        if (_parts.All(p => p.Status == OrderPartStatus.Cancelled
                || (p.Status == OrderPartStatus.Returned && !p.IsBuyerReturn)))
        {
            MarkCancelled(CouldNotDeliver, now);
        }
        else
        {
            CompleteIfDone();
        }

        return Result.Success();
    }

    /// <summary>
    /// Records what the seller found in what came back: a condition for each product, by id. Once
    /// only, and every line that came back needs one.
    /// </summary>
    public Result InspectReturn(
        Guid partId,
        IReadOnlyDictionary<Guid, ReturnCondition> conditions,
        string? note,
        string? inspectedBy,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(conditions);

        var part = _parts.FirstOrDefault(p => p.PublicId == partId);

        if (part is null)
        {
            return Result.Failure(OrderErrors.PartNotFound);
        }

        if (part.Status != OrderPartStatus.Returned || part.ReturnCondition is not null)
        {
            return Result.Failure(OrderErrors.NotAwaitingInspection);
        }

        var cameBack = part.CameBack.Select(x => x.Line).ToList();

        if (cameBack.Any(l => !conditions.ContainsKey(l.ProductId)))
        {
            return Result.Failure(OrderErrors.ConditionForEveryLine);
        }

        foreach (var line in cameBack)
        {
            line.RecordCondition(conditions[line.ProductId]);
        }

        part.RecordInspection(
            cameBack.Any(l => l.ReturnCondition == ReturnCondition.Damaged) ? ReturnCondition.Damaged : ReturnCondition.Good,
            note,
            inspectedBy,
            now);

        return Result.Success();
    }

    /// <summary>Why an order ends when every part came back undelivered. Shown to the buyer.</summary>
    public const string CouldNotDeliver = "The courier could not deliver it, and it went back to the seller.";

    /// <summary>
    /// Cancels a part. Its share of the delivery charge moves to the parts not yet packed, since
    /// the order still ships; a packed part is already booked with the courier at a fixed cash
    /// amount, and a delivered one has already been earned from, so neither can take more. With
    /// nowhere to go, the share is given back - refunded if paid online, or simply not collected.
    /// Cancelling a whole order therefore gives back the whole charge: nothing ships.
    ///
    /// A share moves only between parts alike in whether a free-delivery coupon covers them, so
    /// the buyer never starts paying delivery a coupon lifted, nor stops paying what they paid.
    /// </summary>
    private void CancelPartInternal(OrderPart part, string reason)
    {
        var share = part.DeliveryFee;
        var takers = _parts
            .Where(p => p != part
                && p.FreeDelivery == part.FreeDelivery
                && p.Status is OrderPartStatus.AwaitingPayment or OrderPartStatus.Confirmed)
            .ToList();

        part.SetDeliveryFee(0m);

        if (takers.Count > 0)
        {
            ShareDeliveryFee(share, takers);
            share = 0m;
        }
        else
        {
            ShippingFee -= share;
        }

        // A share the buyer never paid - lifted by a coupon - is not theirs to have back.
        var refund = PaymentStatus == PaymentStatus.Paid ? part.GoodsPaid + (part.FreeDelivery ? 0m : share) : 0m;

        part.Cancel(reason);

        Raise(new OrderPartCancelledDomainEvent(
            PublicId, Number, part.PublicId, part.SellerId, refund, Currency));
    }

    /// <summary>Adds <paramref name="amount"/> of delivery charge to <paramref name="parts"/>, shared as <see cref="DeliveryShares"/> says.</summary>
    private void ShareDeliveryFee(decimal amount, List<OrderPart> parts)
    {
        if (amount == 0m || parts.Count == 0)
        {
            return;
        }

        var shares = DeliveryShares.Split(amount, [.. parts.Select(p => p.Subtotal)]);

        for (var i = 0; i < parts.Count; i++)
        {
            parts[i].SetDeliveryFee(parts[i].DeliveryFee + shares[i]);
        }

        ShippingFee = _parts.Sum(p => p.DeliveryFee);
    }

    /// <summary>
    /// The delivery share the seller is paid for a part: all of it, unless the seller paid for the
    /// free-delivery coupon that lifted it, in which case they waived it.
    /// </summary>
    private decimal SellerEarnsDelivery(OrderPart part) =>
        part.FreeDelivery && CouponFundedBy == "Seller" ? 0m : part.DeliveryFee;

    private void MarkCancelled(string reason, DateTime now)
    {
        Status = OrderStatus.Cancelled;
        CancelledAtUtc = now;
        CancellationReason = reason;
        PaymentDueAtUtc = null;

        Raise(new OrderCancelledDomainEvent(PublicId, Number, reason));
    }

    /// <summary>
    /// The order is complete once every part that went out has reached the buyer. A part the
    /// buyer then sent back still reached them; a cancelled part or an RTO never did, so neither
    /// counts either way.
    /// </summary>
    private void CompleteIfDone()
    {
        var wentOut = _parts
            .Where(p => p.Status != OrderPartStatus.Cancelled
                && (p.IsBuyerReturn || p.Status is not (OrderPartStatus.Returning or OrderPartStatus.Returned)))
            .ToList();

        if (wentOut.Count > 0 && wentOut.All(p => p.Status == OrderPartStatus.Delivered || p.IsBuyerReturn))
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
    int Quantity,
    decimal Discount = 0m);

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

    /// <summary>
    /// What the seller found in what came back, as a whole: Damaged if any line was, else Good.
    /// Null until inspected. Each line keeps its own.
    /// </summary>
    public ReturnCondition? ReturnCondition { get; private set; }

    public string? ReturnNote { get; private set; }

    public string? ReturnInspectedBy { get; private set; }

    public DateTime? ReturnInspectedAtUtc { get; private set; }

    /// <summary>When the courier reported it delivered; null until then.</summary>
    public DateTime? DeliveredAtUtc { get; private set; }

    /// <summary>The last moment the buyer may ask to return it; fixed at delivery.</summary>
    public DateTime? ReturnWindowClosesAtUtc { get; private set; }

    /// <summary>The buyer's request to send it back, if they made one.</summary>
    public ReturnRequest? ReturnRequest { get; private set; }

    /// <summary>
    /// Going or gone back because the buyer's return was approved, as opposed to an RTO: the goods
    /// reached the buyer first, so their money is owed however they paid.
    /// </summary>
    public bool IsBuyerReturn => ReturnRequest?.Status == ReturnRequestStatus.Approved;

    /// <summary>Undelivered and taken back by the courier: none of it reached the buyer.</summary>
    public bool IsRto => Status is OrderPartStatus.Returning or OrderPartStatus.Returned && !IsBuyerReturn;

    /// <summary>
    /// Some or all of it is the buyer's: neither called off nor taken back undelivered. A part the
    /// buyer is returning still counts for the units they keep.
    /// </summary>
    public bool IsKept => Status != OrderPartStatus.Cancelled && !IsRto;

    /// <summary>The goods the buyer keeps, at full price.</summary>
    public decimal KeptSubtotal => _lines.Sum(l => l.UnitPrice * l.KeptQuantity);

    /// <summary>The coupon discount on the goods the buyer keeps.</summary>
    public decimal KeptDiscount => _lines.Sum(l => l.KeptDiscount);

    /// <summary>The lines that came back, with how many of each: all of an RTO, the returned units of a buyer's return.</summary>
    public IEnumerable<(OrderLine Line, int Quantity)> CameBack =>
        IsBuyerReturn
            ? _lines.Where(l => l.ReturnedQuantity > 0).Select(l => (l, l.ReturnedQuantity))
            : _lines.Select(l => (l, l.Quantity));

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    public decimal Subtotal => _lines.Sum(l => l.LineTotal);

    /// <summary>This part's share of the order's delivery charge.</summary>
    public decimal DeliveryFee { get; private set; }

    /// <summary>A free-delivery coupon covers this part: the buyer does not pay its delivery share.</summary>
    public bool FreeDelivery { get; private set; }

    /// <summary>The delivery the buyer pays for this part.</summary>
    public decimal DeliveryPaid => FreeDelivery ? 0m : DeliveryFee;

    /// <summary>The coupon discount on this part's goods.</summary>
    public decimal Discount => _lines.Sum(l => l.Discount);

    /// <summary>What the buyer pays for the goods: what is refunded if they come back.</summary>
    public decimal GoodsPaid => Subtotal - Discount;

    /// <summary>What the buyer pays for this part: its goods, less discount, and its share of delivery. Cash on delivery collects this.</summary>
    public decimal AmountDue => GoodsPaid + DeliveryPaid;

    internal static OrderPart Create(Guid sellerId, OrderPartStatus status, IEnumerable<OrderLineInput> lines)
    {
        var part = new OrderPart { SellerId = sellerId, Status = status };

        part._lines.AddRange(lines.Select(OrderLine.Create));

        return part;
    }

    internal void MoveTo(OrderPartStatus status) => Status = status;

    internal void SetDeliveryFee(decimal fee) => DeliveryFee = fee;

    internal void MakeDeliveryFree() => FreeDelivery = true;

    internal void RecordDelivered(DateTime now, TimeSpan returnWindow)
    {
        DeliveredAtUtc = now;
        ReturnWindowClosesAtUtc = now + returnWindow;
    }

    internal void RequestReturn(ReturnRequest request) => ReturnRequest = request;

    internal void RecordInspection(ReturnCondition condition, string? note, string? inspectedBy, DateTime now)
    {
        ReturnCondition = condition;
        ReturnNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ReturnInspectedBy = inspectedBy;
        ReturnInspectedAtUtc = now;
    }

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

    /// <summary>The coupon discount on this line; zero when no coupon covered it.</summary>
    public decimal Discount { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;

    /// <summary>Units the buyer asked to send back; zero when they asked for none.</summary>
    public int ReturnRequestedQuantity { get; private set; }

    /// <summary>Units of an approved return: no longer the buyer's.</summary>
    public int ReturnedQuantity { get; private set; }

    /// <summary>
    /// The share of <see cref="Discount"/> that was on the returned units. The buyer never paid it,
    /// so it is not refunded.
    /// </summary>
    public decimal ReturnedDiscount { get; private set; }

    /// <summary>
    /// Discount on the kept units taken back from the buyer's refund, because what they kept no
    /// longer met the coupon's minimum order.
    /// </summary>
    public decimal RevokedDiscount { get; private set; }

    /// <summary>What the seller found in the units that came back; null until inspected.</summary>
    public ReturnCondition? ReturnCondition { get; private set; }

    public int KeptQuantity => Quantity - ReturnedQuantity;

    /// <summary>The coupon discount still on the kept units.</summary>
    public decimal KeptDiscount => Discount - ReturnedDiscount - RevokedDiscount;

    /// <summary>What the buyer paid for the returned units: refunded when they are back.</summary>
    public decimal ReturnedPaid => (UnitPrice * ReturnedQuantity) - ReturnedDiscount;

    internal void AskToReturn(int quantity) => ReturnRequestedQuantity = quantity;

    /// <summary>The return of the requested units is approved.</summary>
    internal void Return()
    {
        ReturnedQuantity = ReturnRequestedQuantity;
        ReturnedDiscount = ReturnedQuantity == Quantity
            ? Discount
            : Math.Round(Discount * ReturnedQuantity / Quantity, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Takes back the discount on the kept units, and says how much that was.</summary>
    internal decimal RevokeDiscount()
    {
        var amount = KeptQuantity > 0 ? KeptDiscount : 0m;
        RevokedDiscount += amount;

        return amount;
    }

    internal void RecordCondition(ReturnCondition condition) => ReturnCondition = condition;

    internal static OrderLine Create(OrderLineInput input) => new()
    {
        ProductId = input.ProductId,
        Sku = input.Sku,
        Name = input.Name,
        UnitPrice = input.UnitPrice,
        Quantity = input.Quantity,
        Discount = input.Discount,
    };
}
