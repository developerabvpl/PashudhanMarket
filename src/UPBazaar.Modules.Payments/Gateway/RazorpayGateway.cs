using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Gateway;

/// <summary>
/// Razorpay's Orders API over HTTP.
///
/// Orders are created with automatic capture, so a payment Checkout reports as successful is
/// already captured; nothing here has to capture it later, and an authorised-but-uncaptured
/// payment cannot sit forgotten until Razorpay auto-refunds it.
///
/// Not yet run against Razorpay itself: there are no keys in this environment. The request and
/// response shapes follow Razorpay's published API, and the signature checks are covered by tests.
/// </summary>
internal sealed partial class RazorpayGateway(
    HttpClient http,
    IOptions<RazorpayOptions> options,
    ILogger<RazorpayGateway> logger) : IPaymentGateway
{
    public const string HttpClientName = "razorpay";

    private readonly RazorpayOptions _options = options.Value;

    public string Name => "Razorpay";

    public bool IsEnabled => true;

    public string KeyId => _options.KeyId!;

    public async Task<Result<string>> CreateOrderAsync(
        long amountInPaise,
        string currency,
        string receipt,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest(
                amountInPaise,
                currency,
                receipt,
                PaymentCapture: 1,
                new Dictionary<string, string> { ["order_id"] = orderId.ToString() })),
        };

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.KeyId}:{_options.KeySecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogCreateOrderFailed(logger, receipt, (int)response.StatusCode);

                return Result.Failure<string>(PaymentErrors.GatewayUnavailable);
            }

            var created = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(cancellationToken);

            return created?.Id is { Length: > 0 } id
                ? id
                : Result.Failure<string>(PaymentErrors.GatewayUnavailable);
        }
        catch (HttpRequestException exception)
        {
            LogCreateOrderError(logger, exception, receipt);

            return Result.Failure<string>(PaymentErrors.GatewayUnavailable);
        }
    }

    public bool IsPaymentSignatureValid(string gatewayOrderId, string gatewayPaymentId, string signature) =>
        RazorpaySignature.Matches(
            RazorpaySignature.ForPayment(gatewayOrderId, gatewayPaymentId, _options.KeySecret!),
            signature);

    public bool IsWebhookSignatureValid(string rawBody, string signature) =>
        RazorpaySignature.Matches(RazorpaySignature.ForWebhook(rawBody, _options.WebhookSecret!), signature);

    private sealed record CreateOrderRequest(
        [property: JsonPropertyName("amount")] long Amount,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("receipt")] string Receipt,
        [property: JsonPropertyName("payment_capture")] int PaymentCapture,
        [property: JsonPropertyName("notes")] IReadOnlyDictionary<string, string> Notes);

    private sealed record CreateOrderResponse([property: JsonPropertyName("id")] string? Id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Razorpay refused to create an order for {Receipt}: HTTP {Status}")]
    private static partial void LogCreateOrderFailed(ILogger logger, string receipt, int status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not reach Razorpay to create an order for {Receipt}")]
    private static partial void LogCreateOrderError(ILogger logger, Exception exception, string receipt);
}
