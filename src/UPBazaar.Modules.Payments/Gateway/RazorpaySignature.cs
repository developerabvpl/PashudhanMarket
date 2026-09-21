using System.Security.Cryptography;
using System.Text;

namespace UPBazaar.Modules.Payments.Gateway;

/// <summary>
/// Razorpay's two signatures, both a hex HMAC-SHA256.
///
/// A payment signature is keyed with the API key secret over <c>order_id|payment_id</c>; a webhook
/// signature is keyed with the webhook secret over the raw request body. Compared in constant time
/// so the comparison itself does not leak how much of a forged signature was right.
/// </summary>
public static class RazorpaySignature
{
    public static string ForPayment(string gatewayOrderId, string gatewayPaymentId, string keySecret) =>
        Compute($"{gatewayOrderId}|{gatewayPaymentId}", keySecret);

    public static string ForWebhook(string rawBody, string webhookSecret) => Compute(rawBody, webhookSecret);

    public static bool Matches(string expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(actual.Trim().ToLowerInvariant()));
    }

    private static string Compute(string payload, string secret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));

        return Convert.ToHexStringLower(hash);
    }
}
