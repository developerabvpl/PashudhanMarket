using System.Security.Cryptography;
using System.Text;

namespace UPBazaar.IntegrationTests.Infrastructure;

/// <summary>
/// Mirrors what the gateway does to a webhook body. Kept separate from the fake gateway so a
/// test cannot pass by accidentally sharing the production implementation's bug.
/// </summary>
public static class WebhookSignature
{
    public static string Compute(string payload, string secret) =>
        Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
}
