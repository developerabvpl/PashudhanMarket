namespace UPBazaar.Infrastructure.ExternalServices.Payments;

public sealed record GatewayOrderRequest(
    string IdempotencyKey,
    decimal Amount,
    string Currency,
    string Reference);

public sealed record GatewayOrder(
    string GatewayOrderId,
    string Status,
    decimal Amount,
    string Currency);

public sealed record GatewayRefundRequest(
    string IdempotencyKey,
    string GatewayPaymentId,
    decimal Amount,
    string Reason);

public sealed record GatewayRefund(string GatewayRefundId, string Status, decimal Amount);

/// <summary>
/// Payment provider boundary. Every implementation must be safe to call twice with the same
/// IdempotencyKey and return the same result.
/// </summary>
public interface IPaymentGateway
{
    string Provider { get; }

    Task<GatewayOrder> CreateOrderAsync(GatewayOrderRequest request, CancellationToken cancellationToken);

    Task<GatewayRefund> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken);

    /// <summary>Validates the provider signature on a raw webhook body.</summary>
    bool VerifyWebhookSignature(string payload, string? signature);
}
