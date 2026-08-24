using System.Text;
using System.Text.Json;

namespace UPBazaar.IntegrationTests.Infrastructure;

/// <summary>
/// Builds a Razorpay-shaped webhook request. The signature is computed over the exact bytes
/// that get sent, which is the only way it can prove the endpoint reads the raw body.
/// </summary>
public static class GatewayWebhook
{
    public static HttpRequestMessage Build(
        string eventType,
        string gatewayOrderId,
        string gatewayPaymentId,
        string eventId,
        string? signatureOverride = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            @event = eventType,
            payload = new
            {
                payment = new
                {
                    entity = new
                    {
                        id = gatewayPaymentId,
                        order_id = gatewayOrderId,
                    },
                },
            },
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhooks/razorpay")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add("X-Razorpay-Event-Id", eventId);
        request.Headers.Add("X-Razorpay-Signature", signatureOverride ?? ApiFixture.SignWebhook(body));

        return request;
    }

    public static string NewGatewayPaymentId() => $"pay_{Guid.NewGuid():N}"[..20];
}
