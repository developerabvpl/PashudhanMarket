using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace UPBazaar.Infrastructure.ExternalServices.Payments;

/// <summary>
/// Sandbox gateway. Deterministic ids keyed on the idempotency key, and a real HMAC check so
/// webhook signature handling is exercised the same way it will be in production.
/// </summary>
public sealed class FakePaymentGateway(IOptions<ExternalServicesOptions> options) : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, GatewayOrder> _orders = new();
    private readonly ConcurrentDictionary<string, GatewayRefund> _refunds = new();

    public string Provider => "fake";

    public Task<GatewayOrder> CreateOrderAsync(
        GatewayOrderRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(_orders.GetOrAdd(
            request.IdempotencyKey,
            _ => new GatewayOrder(
                $"order_{Deterministic(request.IdempotencyKey)}",
                "created",
                request.Amount,
                request.Currency)));

    public Task<GatewayRefund> RefundAsync(
        GatewayRefundRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(_refunds.GetOrAdd(
            request.IdempotencyKey,
            _ => new GatewayRefund(
                $"rfnd_{Deterministic(request.IdempotencyKey)}",
                "processed",
                request.Amount)));

    public bool VerifyWebhookSignature(string payload, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Sign(payload, options.Value.Razorpay.WebhookSecret)),
            Encoding.UTF8.GetBytes(signature));
    }

    /// <summary>Produces the signature a caller must send. Test-only convenience.</summary>
    public string SignForTest(string payload) => Sign(payload, options.Value.Razorpay.WebhookSecret);

    private static string Sign(string payload, string secret) =>
        Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();

    private static string Deterministic(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..14].ToLowerInvariant();
}
