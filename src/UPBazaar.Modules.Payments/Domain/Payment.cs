using UPBazaar.Modules.Payments.Contracts.Events;
using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Domain;

public enum PaymentStatus
{
    Created = 0,
    Captured = 1,
    Failed = 2,
    Refunded = 3,
    PartiallyRefunded = 4,
}

public sealed class Payment : AggregateRoot, IAuditable
{
    /// <summary>Marketplace commission withheld from the seller payout.</summary>
    public const decimal CommissionRate = 0.05m;

    private readonly List<Refund> _refunds = [];
    private readonly List<SettlementLine> _settlementLines = [];

    private Payment()
    {
    }

    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; } = null!;

    public Guid SellerId { get; private set; }

    public string Provider { get; private set; } = null!;

    public string GatewayOrderId { get; private set; } = null!;

    public string? GatewayPaymentId { get; private set; }

    public decimal Amount { get; private set; }

    public decimal RefundedAmount { get; private set; }

    public string Currency { get; private set; } = "INR";

    public PaymentStatus Status { get; private set; }

    public DateTime? CapturedAtUtc { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>Concurrency token: a webhook retry and an operator refund can collide here.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<Refund> Refunds => _refunds.AsReadOnly();

    public IReadOnlyList<SettlementLine> SettlementLines => _settlementLines.AsReadOnly();

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>Captured minus everything already refunded.</summary>
    public decimal RefundableAmount => Amount - RefundedAmount;

    public static Payment Open(
        Guid orderId,
        string orderNumber,
        Guid sellerId,
        string provider,
        string gatewayOrderId,
        decimal amount,
        string currency) => new()
        {
            OrderId = orderId,
            OrderNumber = orderNumber,
            SellerId = sellerId,
            Provider = provider,
            GatewayOrderId = gatewayOrderId,
            Amount = amount,
            Currency = currency.ToUpperInvariant(),
            Status = PaymentStatus.Created,
        };

    /// <summary>
    /// Money confirmed by the gateway. Idempotent: a webhook redelivery for a payment that is
    /// already captured is a no-op rather than a second settlement line.
    /// </summary>
    public Result Capture(string gatewayPaymentId, DateTime capturedAtUtc)
    {
        if (Status == PaymentStatus.Captured && GatewayPaymentId == gatewayPaymentId)
        {
            return Result.Success();
        }

        if (Status != PaymentStatus.Created)
        {
            return Result.Failure(PaymentErrors.NotCapturable);
        }

        GatewayPaymentId = gatewayPaymentId;
        Status = PaymentStatus.Captured;
        CapturedAtUtc = capturedAtUtc;

        _settlementLines.Add(SettlementLine.Create(SellerId, Amount, CommissionRate, Currency));

        Raise(new PaymentCapturedDomainEvent(PublicId, OrderId, Amount, Currency));

        return Result.Success();
    }

    public Result Fail(string reason)
    {
        if (Status is PaymentStatus.Captured or PaymentStatus.Refunded)
        {
            return Result.Failure(PaymentErrors.NotCapturable);
        }

        Status = PaymentStatus.Failed;
        FailureReason = reason;

        Raise(new PaymentFailedDomainEvent(PublicId, OrderId, reason));

        return Result.Success();
    }

    public Result<Refund> IssueRefund(string gatewayRefundId, decimal amount, string reason)
    {
        if (Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            return Result.Failure<Refund>(PaymentErrors.NotRefundable);
        }

        if (amount <= 0)
        {
            return Result.Failure<Refund>(PaymentErrors.InvalidAmount);
        }

        if (amount > RefundableAmount)
        {
            return Result.Failure<Refund>(PaymentErrors.RefundExceedsCaptured);
        }

        var refund = Refund.Create(gatewayRefundId, amount, reason);
        _refunds.Add(refund);
        RefundedAmount += amount;

        Status = RefundedAmount >= Amount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;

        // The seller does not get paid for money that went back to the customer.
        foreach (var line in _settlementLines.Where(l => l.Status == SettlementStatus.Pending))
        {
            line.Reverse();
        }

        Raise(new RefundIssuedDomainEvent(refund.PublicId, PublicId, OrderId, amount));

        return refund;
    }
}
