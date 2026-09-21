using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Domain;

/// <summary>Whether the money has been taken.</summary>
public enum PaymentStatus
{
    /// <summary>A gateway order exists and the buyer can pay against it.</summary>
    Created = 0,

    /// <summary>The gateway has captured the money.</summary>
    Paid = 1,
}

/// <summary>What Orders made of a captured payment.</summary>
public enum OrderOutcome
{
    /// <summary>Not yet reported to Orders, or Orders was busy; the settlement job will retry.</summary>
    Pending = 0,

    /// <summary>The order accepted it and is confirmed.</summary>
    Confirmed = 1,

    /// <summary>
    /// The order would not take it - cancelled already, past its deadline, a different amount -
    /// so the whole payment is owed back.
    /// </summary>
    Refused = 2,
}

/// <summary>
/// One attempt to pay for one order, from the gateway order the buyer pays against to the money
/// landing and the order accepting it.
///
/// Taking money and confirming the order are two steps in two modules, so a payment remembers
/// which of them has happened. A payment that is Paid with its outcome still Pending is money in
/// the bank that no order has claimed; the settlement job finds those and finishes the job. That
/// is what makes it safe for the browser to close, or the process to die, between the two steps.
/// </summary>
public sealed class Payment : AggregateRoot, IAuditable
{
    private readonly List<Refund> _refunds = [];

    private Payment()
    {
    }

    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    public Guid BuyerId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public PaymentStatus Status { get; private set; }

    public OrderOutcome OrderOutcome { get; private set; }

    /// <summary>Why Orders refused it, when it did.</summary>
    public string? OrderOutcomeReason { get; private set; }

    /// <summary>Razorpay, or Fake in development.</summary>
    public string Gateway { get; private set; } = string.Empty;

    /// <summary>The gateway's order id; unique, and how webhooks find this payment.</summary>
    public string GatewayOrderId { get; private set; } = string.Empty;

    /// <summary>The gateway's payment id, once paid.</summary>
    public string? GatewayPaymentId { get; private set; }

    /// <summary>When the order is cancelled if this has not been paid.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? PaidAtUtc { get; private set; }

    /// <summary>Why the most recent attempt at the gateway failed, for support.</summary>
    public string? LastFailure { get; private set; }

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    /// <summary>Optimistic concurrency: the browser and a webhook reporting the same payment must not both act.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>Money recorded as owed back and not yet returned.</summary>
    public decimal RefundDue => _refunds.Where(r => r.Status == RefundStatus.Due).Sum(r => r.Amount);

    public static Payment Create(
        Guid orderId,
        string orderNumber,
        Guid buyerId,
        decimal amount,
        string currency,
        string gateway,
        string gatewayOrderId,
        DateTime expiresAtUtc) => new()
    {
        OrderId = orderId,
        OrderNumber = orderNumber,
        BuyerId = buyerId,
        Amount = amount,
        Currency = currency,
        Gateway = gateway,
        GatewayOrderId = gatewayOrderId,
        ExpiresAtUtc = expiresAtUtc,
        Status = PaymentStatus.Created,
        OrderOutcome = OrderOutcome.Pending,
    };

    /// <summary>
    /// The gateway has the money. Reporting the same gateway payment again is a no-op, since the
    /// browser and the webhook both report every payment.
    /// </summary>
    public Result MarkPaid(string gatewayPaymentId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayPaymentId);

        if (Status == PaymentStatus.Paid)
        {
            return GatewayPaymentId == gatewayPaymentId
                ? Result.Success()
                : Result.Failure(PaymentErrors.PaidWithAnotherPayment);
        }

        Status = PaymentStatus.Paid;
        GatewayPaymentId = gatewayPaymentId;
        PaidAtUtc = now;
        LastFailure = null;

        return Result.Success();
    }

    /// <summary>A card was declined or the buyer gave up. The payment stays open for another try.</summary>
    public void RecordFailedAttempt(string reason)
    {
        if (Status == PaymentStatus.Created)
        {
            LastFailure = reason.Length > 500 ? reason[..500] : reason;
        }
    }

    public void RecordOrderConfirmed()
    {
        if (OrderOutcome == OrderOutcome.Pending)
        {
            OrderOutcome = OrderOutcome.Confirmed;
        }
    }

    /// <summary>
    /// Orders would not accept the money. It all goes back: the buyer has nothing to show for it.
    /// </summary>
    public void RecordOrderRefused(string reason, DateTime now)
    {
        if (OrderOutcome != OrderOutcome.Pending)
        {
            return;
        }

        OrderOutcome = OrderOutcome.Refused;
        OrderOutcomeReason = reason;

        _refunds.Add(Refund.Create(orderPartId: null, Amount, $"Payment could not be applied to the order: {reason}", now));
    }

    /// <summary>
    /// Records that part of a paid order was cancelled and its share is owed back. Recording the
    /// same part twice changes nothing, because the event that drives this can be delivered twice.
    /// </summary>
    public Result RecordPartRefundDue(Guid orderPartId, decimal amount, string reason, DateTime now)
    {
        if (_refunds.Any(r => r.OrderPartId == orderPartId))
        {
            return Result.Success();
        }

        // A refused payment already owes everything back; a part cancelled on top of that owes nothing more.
        if (OrderOutcome == OrderOutcome.Refused)
        {
            return Result.Success();
        }

        if (_refunds.Sum(r => r.Amount) + amount > Amount)
        {
            return Result.Failure(PaymentErrors.RefundExceedsPayment);
        }

        _refunds.Add(Refund.Create(orderPartId, amount, reason, now));

        return Result.Success();
    }
}

/// <summary>Whether owed money has gone back.</summary>
public enum RefundStatus
{
    Due = 0,
    Refunded = 1,
}

/// <summary>
/// Money owed back to the buyer out of one payment.
///
/// Recorded here, made by hand: staff refund in the Razorpay dashboard and then record the
/// refund id against it. Automatic refunds can replace the hand step later without the record
/// changing shape.
/// </summary>
public sealed class Refund : Entity
{
    private Refund()
    {
    }

    public long PaymentId { get; private set; }

    /// <summary>The seller's part of the order that was cancelled; null when the whole payment is owed.</summary>
    public Guid? OrderPartId { get; private set; }

    public decimal Amount { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public RefundStatus Status { get; private set; }

    public string? GatewayRefundId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? RefundedAtUtc { get; private set; }

    /// <summary>User id of whoever recorded the refund as made.</summary>
    public string? RefundedBy { get; private set; }

    internal static Refund Create(Guid? orderPartId, decimal amount, string reason, DateTime now) => new()
    {
        OrderPartId = orderPartId,
        Amount = amount,
        Reason = reason.Length > 500 ? reason[..500] : reason,
        Status = RefundStatus.Due,
        CreatedAtUtc = now,
    };

    public Result MarkRefunded(string gatewayRefundId, string? by, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayRefundId);

        if (Status == RefundStatus.Refunded)
        {
            return Result.Failure(PaymentErrors.AlreadyRefunded);
        }

        Status = RefundStatus.Refunded;
        GatewayRefundId = gatewayRefundId.Trim();
        RefundedAtUtc = now;
        RefundedBy = by;

        return Result.Success();
    }
}

/// <summary>
/// A webhook delivery already handled. Razorpay retries a delivery until it gets a 2xx, and may
/// deliver the same event more than once anyway; its id is recorded so the second copy is ignored.
/// </summary>
public sealed class ProcessedWebhookEvent : Entity
{
    private ProcessedWebhookEvent()
    {
    }

    public string EventId { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;

    public DateTime ReceivedAtUtc { get; private set; }

    public static ProcessedWebhookEvent Create(string eventId, string eventType, DateTime now) => new()
    {
        EventId = eventId,
        EventType = eventType,
        ReceivedAtUtc = now,
    };
}
