using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Settlements.Domain;

/// <summary>What an earning is for. A delivered parcel can make one of each.</summary>
public enum EarningKind
{
    /// <summary>The goods: commission is taken, and a return undoes it.</summary>
    Sale = 0,

    /// <summary>
    /// The seller's share of the buyer's delivery charge. No commission, and a return leaves it
    /// alone: the delivery was made, and the buyer is not refunded for it.
    /// </summary>
    Delivery = 1,

    /// <summary>
    /// What a courier trip cost, taken off what the seller is paid: a negative amount, or positive
    /// when a correction gives some back. Owed at once, whatever happens to the goods, and carried
    /// into later payouts when there is not enough to take it from yet.
    /// </summary>
    CourierCost = 2,
}

/// <summary>Where a seller's earning from one parcel stands.</summary>
public enum EarningStatus
{
    /// <summary>
    /// Delivered. Payable once the buyer's return window has closed, and paid in the first payout
    /// run after that.
    /// </summary>
    Accruing = 0,

    /// <summary>The buyer has asked to return it; not payable until the request is decided.</summary>
    OnHold = 1,

    /// <summary>The buyer's return was accepted: the sale is undone and nothing is owed.</summary>
    Cancelled = 2,

    /// <summary>Included in a payout.</summary>
    Settled = 3,
}

/// <summary>
/// What a seller earns from one delivered parcel: the sale, less the platform's commission and
/// the taxes withheld, each worked out once, at delivery, at the rates in force then. A later
/// change of rates leaves it alone, so a seller's statement never shifts under them.
///
/// Every amount is rounded to the paisa on its own, and the net is what is left, so the lines
/// always add up to the sale on a statement.
/// </summary>
public sealed class Earning : Entity
{
    private Earning()
    {
    }

    public Guid SellerId { get; private set; }

    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>The parcel earned from; one earning of each kind per parcel.</summary>
    public Guid OrderPartId { get; private set; }

    public EarningKind Kind { get; private set; }

    /// <summary>For a courier cost, which trip: Delivery, Rto or ReturnPickup.</summary>
    public string? Detail { get; private set; }

    /// <summary>
    /// For a courier cost, the shipment's charge it came from, so a redelivered event is not
    /// charged twice. Null for sales and delivery shares, which are one per parcel anyway.
    /// </summary>
    public string? Reference { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    /// <summary>What the buyer paid for the parcel's goods, or for its delivery.</summary>
    public decimal GrossAmount { get; private set; }

    public decimal CommissionPercent { get; private set; }

    public decimal CommissionAmount { get; private set; }

    public decimal TcsPercent { get; private set; }

    public decimal TcsAmount { get; private set; }

    public decimal TdsPercent { get; private set; }

    public decimal TdsAmount { get; private set; }

    /// <summary>What the seller is paid: the gross less commission and both taxes.</summary>
    public decimal NetAmount { get; private set; }

    public DateTime DeliveredAtUtc { get; private set; }

    /// <summary>When the buyer's return window closes; not payable before.</summary>
    public DateTime PayableFromUtc { get; private set; }

    public EarningStatus Status { get; private set; }

    /// <summary>The payout it was paid in, once settled.</summary>
    public long? PayoutId { get; private set; }

    /// <summary>Optimistic concurrency: a return landing while a payout run takes the earning must not both win.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>The sale of a parcel's goods, less commission and the taxes withheld.</summary>
    public static Earning Create(
        Guid sellerId,
        Guid orderId,
        string orderNumber,
        Guid orderPartId,
        decimal grossAmount,
        string currency,
        DateTime deliveredAtUtc,
        DateTime payableFromUtc,
        EarningRates rates)
    {
        ArgumentNullException.ThrowIfNull(rates);

        return Make(EarningKind.Sale, sellerId, orderId, orderNumber, orderPartId, grossAmount, currency, deliveredAtUtc, payableFromUtc, rates);
    }

    /// <summary>
    /// The seller's share of the delivery charge. Commission is on goods only, so none is taken
    /// here; the taxes are withheld as on any other amount paid to the seller through the
    /// platform - to be confirmed by the accountant.
    /// </summary>
    public static Earning ForDelivery(
        Guid sellerId,
        Guid orderId,
        string orderNumber,
        Guid orderPartId,
        decimal deliveryAmount,
        string currency,
        DateTime deliveredAtUtc,
        DateTime payableFromUtc,
        EarningRates rates)
    {
        ArgumentNullException.ThrowIfNull(rates);

        return Make(
            EarningKind.Delivery,
            sellerId,
            orderId,
            orderNumber,
            orderPartId,
            deliveryAmount,
            currency,
            deliveredAtUtc,
            payableFromUtc,
            rates with { CommissionPercent = 0m });
    }

    /// <summary>
    /// A courier charge taken off the seller's pay: no commission or tax on it, and payable - that
    /// is, deducted - from the next payout run.
    /// </summary>
    /// <param name="sellerId">Who pays it.</param>
    /// <param name="orderId">The order the parcel belongs to.</param>
    /// <param name="orderNumber">Its number.</param>
    /// <param name="orderPartId">The parcel.</param>
    /// <param name="trip">Delivery, Rto or ReturnPickup.</param>
    /// <param name="amount">What the courier charged; negative when a correction gives some back.</param>
    /// <param name="currency">ISO currency code.</param>
    /// <param name="now">When it was charged; it is deducted from the next payout run.</param>
    /// <param name="reference">The shipment charge it came from, unique per charge.</param>
    public static Earning ForCourierCost(
        Guid sellerId,
        Guid orderId,
        string orderNumber,
        Guid orderPartId,
        string trip,
        decimal amount,
        string currency,
        DateTime now,
        string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var earning = Make(
            EarningKind.CourierCost,
            sellerId,
            orderId,
            orderNumber,
            orderPartId,
            -amount,
            currency,
            now,
            now,
            new EarningRates(0m, 0m, 0m));

        earning.Detail = trip;
        earning.Reference = reference;

        return earning;
    }

    private static Earning Make(
        EarningKind kind,
        Guid sellerId,
        Guid orderId,
        string orderNumber,
        Guid orderPartId,
        decimal grossAmount,
        string currency,
        DateTime deliveredAtUtc,
        DateTime payableFromUtc,
        EarningRates rates)
    {
        var commission = Paise(grossAmount * rates.CommissionPercent / 100m);
        var tcs = Paise(grossAmount * rates.TcsPercent / 100m);
        var tds = Paise(grossAmount * rates.TdsPercent / 100m);

        return new Earning
        {
            Kind = kind,
            SellerId = sellerId,
            OrderId = orderId,
            OrderNumber = orderNumber,
            OrderPartId = orderPartId,
            Currency = currency,
            GrossAmount = grossAmount,
            CommissionPercent = rates.CommissionPercent,
            CommissionAmount = commission,
            TcsPercent = rates.TcsPercent,
            TcsAmount = tcs,
            TdsPercent = rates.TdsPercent,
            TdsAmount = tds,
            NetAmount = grossAmount - commission - tcs - tds,
            DeliveredAtUtc = deliveredAtUtc,
            PayableFromUtc = payableFromUtc,
            Status = EarningStatus.Accruing,
        };
    }

    public bool IsPayable(DateTime now) => Status == EarningStatus.Accruing && PayableFromUtc <= now;

    /// <summary>A return was asked for. Only an earning not yet paid can be held; a paid one has gone.</summary>
    public void Hold()
    {
        if (Status == EarningStatus.Accruing)
        {
            Status = EarningStatus.OnHold;
        }
    }

    /// <summary>The return was refused: payable again once the window has closed.</summary>
    public void Release()
    {
        if (Status == EarningStatus.OnHold)
        {
            Status = EarningStatus.Accruing;
        }
    }

    /// <summary>The return was accepted. Nothing is owed for goods the seller gets back.</summary>
    public void Cancel()
    {
        if (Status is EarningStatus.Accruing or EarningStatus.OnHold)
        {
            Status = EarningStatus.Cancelled;
        }
    }

    internal void Settle() => Status = EarningStatus.Settled;

    private static decimal Paise(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

/// <summary>The percentages an earning is worked out at.</summary>
public sealed record EarningRates(decimal CommissionPercent, decimal TcsPercent, decimal TdsPercent);
