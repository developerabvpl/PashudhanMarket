using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Payments.Domain;

public enum RefundStatus
{
    Requested = 0,
    Processed = 1,
    Failed = 2,
}

public sealed class Refund : Entity, IAuditable
{
    private Refund()
    {
    }

    public long PaymentId { get; private set; }

    public string GatewayRefundId { get; private set; } = null!;

    public decimal Amount { get; private set; }

    public string Reason { get; private set; } = null!;

    public RefundStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    internal static Refund Create(string gatewayRefundId, decimal amount, string reason) => new()
    {
        GatewayRefundId = gatewayRefundId,
        Amount = amount,
        Reason = reason,
        Status = RefundStatus.Processed,
    };
}
