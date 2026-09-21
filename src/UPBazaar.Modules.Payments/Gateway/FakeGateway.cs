using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Gateway;

/// <summary>
/// Razorpay's stand-in for development and tests.
///
/// It keeps the real shape - gateway order ids, HMAC signatures over the same payloads - so the
/// verification and webhook code paths run unchanged; only the secrets are fixed and public. That
/// is why it is registered solely in Development and Testing: anyone can sign a fake payment.
/// </summary>
public sealed class FakeGateway : IPaymentGateway
{
    public const string GatewayName = "Fake";

    /// <summary>Known to everyone, deliberately: tests and the dev-only "simulate payment" sign with it.</summary>
    public const string KeySecret = "fake_key_secret";

    public const string WebhookSecret = "fake_webhook_secret";

    public string Name => GatewayName;

    public bool IsEnabled => true;

    public string KeyId => string.Empty;

    public Task<Result<string>> CreateOrderAsync(
        long amountInPaise,
        string currency,
        string receipt,
        Guid orderId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success($"order_fake_{Guid.NewGuid():N}"[..26]));

    public bool IsPaymentSignatureValid(string gatewayOrderId, string gatewayPaymentId, string signature) =>
        RazorpaySignature.Matches(RazorpaySignature.ForPayment(gatewayOrderId, gatewayPaymentId, KeySecret), signature);

    public bool IsWebhookSignatureValid(string rawBody, string signature) =>
        RazorpaySignature.Matches(RazorpaySignature.ForWebhook(rawBody, WebhookSecret), signature);
}

/// <summary>
/// What a server without Razorpay keys gets: online payment reported as off, so the storefront
/// offers cash on delivery only, and every call refused rather than half-working.
/// </summary>
internal sealed class UnconfiguredGateway : IPaymentGateway
{
    public string Name => "None";

    public bool IsEnabled => false;

    public string KeyId => string.Empty;

    public Task<Result<string>> CreateOrderAsync(
        long amountInPaise,
        string currency,
        string receipt,
        Guid orderId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<string>(Domain.PaymentErrors.OnlineDisabled));

    public bool IsPaymentSignatureValid(string gatewayOrderId, string gatewayPaymentId, string signature) => false;

    public bool IsWebhookSignatureValid(string rawBody, string signature) => false;
}
