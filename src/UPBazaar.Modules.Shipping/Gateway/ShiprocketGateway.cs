using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Gateway;

/// <summary>
/// Shiprocket's API over HTTP.
///
/// Shiprocket authenticates with a token from signing in, valid for ten days. It is kept in
/// <see cref="ShiprocketTokenCache"/> for the life of the process and fetched again on a 401, so a
/// long-running server neither signs in on every call nor breaks when the token lapses.
///
/// Not yet run against Shiprocket: there are no credentials in this environment. The request and
/// response shapes follow Shiprocket's published API.
/// </summary>
internal sealed partial class ShiprocketGateway(
    HttpClient http,
    ShiprocketTokenCache tokens,
    IOptions<ShiprocketOptions> options,
    ILogger<ShiprocketGateway> logger) : ICourierGateway
{
    private readonly ShiprocketOptions _options = options.Value;

    public string Name => "Shiprocket";

    public bool IsEnabled => true;

    public async Task<Result<CarrierOrder>> CreateOrderAsync(
        CourierOrderRequest request,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            order_id = request.Reference,
            order_date = request.OrderDateUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            pickup_location = request.PickupLocation,
            billing_customer_name = request.CustomerName,
            billing_last_name = "",
            billing_address = request.AddressLine1,
            billing_address_2 = request.AddressLine2 ?? "",
            billing_city = request.City,
            billing_pincode = request.Pincode,
            billing_state = request.State,
            billing_country = "India",
            billing_phone = request.CustomerPhone,
            shipping_is_billing = true,
            order_items = request.Items.Select(i => new
            {
                name = i.Name,
                sku = i.Sku,
                units = i.Units,
                selling_price = i.SellingPrice,
            }),
            payment_method = request.CashOnDelivery ? "COD" : "Prepaid",
            sub_total = request.SubTotal,

            // The buyer's delivery charge: Shiprocket adds it to the sub-total for what it
            // collects on a cash-on-delivery parcel.
            shipping_charges = request.ShippingCharges,
            length = request.LengthCm,
            breadth = request.BreadthCm,
            height = request.HeightCm,
            weight = request.WeightGrams / 1000m,
        };

        var response = await SendAsync<CreateOrderResponse>(HttpMethod.Post, "orders/create/adhoc", body, cancellationToken);

        return response.IsSuccess && response.Value is { OrderId: > 0, ShipmentId: > 0 } created
            ? new CarrierOrder(created.OrderId.ToString(CultureInfo.InvariantCulture), created.ShipmentId.ToString(CultureInfo.InvariantCulture))
            : Result.Failure<CarrierOrder>(ShippingErrors.CourierUnavailable);
    }

    public async Task<Result<CarrierAwb>> AssignAwbAsync(string carrierShipmentId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<AssignAwbResponse>(
            HttpMethod.Post, "courier/assign/awb", new { shipment_id = long.Parse(carrierShipmentId, CultureInfo.InvariantCulture) }, cancellationToken);

        return response.IsSuccess && response.Value?.Response?.Data is { AwbCode.Length: > 0 } data
            ? new CarrierAwb(data.AwbCode, data.CourierName ?? "Courier")
            : Result.Failure<CarrierAwb>(ShippingErrors.CourierUnavailable);
    }

    public async Task<Result> RequestPickupAsync(string carrierShipmentId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<JsonElement>(
            HttpMethod.Post, "courier/generate/pickup", new { shipment_id = new[] { long.Parse(carrierShipmentId, CultureInfo.InvariantCulture) } }, cancellationToken);

        return response.IsSuccess ? Result.Success() : Result.Failure(ShippingErrors.CourierUnavailable);
    }

    /// <remarks>
    /// Shiprocket's return order names both addresses in full: "pickup" is the buyer, "shipping" is
    /// where it goes back to. Its pickup location names are not used, since the seller's
    /// registered address need not be one. Email fields are left blank: buyers sign in by mobile.
    /// </remarks>
    public async Task<Result<CarrierOrder>> CreateReturnOrderAsync(
        CourierReturnRequest request,
        CancellationToken cancellationToken)
    {
        var from = request.CollectFrom;
        var to = request.DeliverTo;

        var body = new
        {
            order_id = request.Reference,
            order_date = request.OrderDateUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            pickup_customer_name = from.Name,
            pickup_last_name = "",
            pickup_address = from.Line1,
            pickup_address_2 = from.Line2 ?? "",
            pickup_city = from.City,
            pickup_state = from.State,
            pickup_country = "India",
            pickup_pincode = from.Pincode,
            pickup_email = "",
            pickup_phone = from.Phone,
            shipping_customer_name = to.Name,
            shipping_last_name = "",
            shipping_address = to.Line1,
            shipping_address_2 = to.Line2 ?? "",
            shipping_city = to.City,
            shipping_state = to.State,
            shipping_country = "India",
            shipping_pincode = to.Pincode,
            shipping_email = "",
            shipping_isd_code = "91",
            shipping_phone = to.Phone,
            order_items = request.Items.Select(i => new
            {
                name = i.Name,
                sku = i.Sku,
                units = i.Units,
                selling_price = i.SellingPrice,
            }),
            payment_method = "Prepaid",
            total_discount = 0,
            sub_total = request.SubTotal,
            length = request.LengthCm,
            breadth = request.BreadthCm,
            height = request.HeightCm,
            weight = request.WeightGrams / 1000m,
        };

        var response = await SendAsync<CreateOrderResponse>(HttpMethod.Post, "orders/create/return", body, cancellationToken);

        return response.IsSuccess && response.Value is { OrderId: > 0, ShipmentId: > 0 } created
            ? new CarrierOrder(created.OrderId.ToString(CultureInfo.InvariantCulture), created.ShipmentId.ToString(CultureInfo.InvariantCulture))
            : Result.Failure<CarrierOrder>(ShippingErrors.CourierUnavailable);
    }

    public async Task<Result<CarrierAwb>> AssignReturnAwbAsync(string carrierShipmentId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<AssignAwbResponse>(
            HttpMethod.Post,
            "courier/assign/awb",
            new { shipment_id = long.Parse(carrierShipmentId, CultureInfo.InvariantCulture), is_return = 1 },
            cancellationToken);

        return response.IsSuccess && response.Value?.Response?.Data is { AwbCode.Length: > 0 } data
            ? new CarrierAwb(data.AwbCode, data.CourierName ?? "Courier")
            : Result.Failure<CarrierAwb>(ShippingErrors.CourierUnavailable);
    }

    public async Task<Result> CancelAsync(string carrierOrderId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<JsonElement>(
            HttpMethod.Post, "orders/cancel", new { ids = new[] { long.Parse(carrierOrderId, CultureInfo.InvariantCulture) } }, cancellationToken);

        return response.IsSuccess ? Result.Success() : Result.Failure(ShippingErrors.CourierUnavailable);
    }

    public bool IsWebhookTokenValid(string? token) =>
        token is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(_options.WebhookToken!));

    public string TrackingUrl(string awb) => $"https://shiprocket.co/tracking/{Uri.EscapeDataString(awb)}";

    /// <summary>One authenticated call, signing in again once if Shiprocket says the token has lapsed.</summary>
    private async Task<Result<T?>> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var token = await tokens.GetAsync(SignInAsync, forceRefresh: attempt > 1, cancellationToken);

            if (token is null)
            {
                return Result.Failure<T?>(ShippingErrors.CourierUnavailable);
            }

            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try
            {
                using var response = await http.SendAsync(request, cancellationToken);

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    LogCallFailed(logger, path, (int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));

                    return Result.Failure<T?>(ShippingErrors.CourierUnavailable);
                }

                return Result.Success(await response.Content.ReadFromJsonAsync<T>(cancellationToken));
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                LogCallError(logger, exception, path);

                return Result.Failure<T?>(ShippingErrors.CourierUnavailable);
            }
        }

        return Result.Failure<T?>(ShippingErrors.CourierUnavailable);
    }

    private async Task<string?> SignInAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                "auth/login", new { email = _options.Email, password = _options.Password }, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogSignInFailed(logger, (int)response.StatusCode);

                return null;
            }

            return (await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken))?.Token;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            LogCallError(logger, exception, "auth/login");

            return null;
        }
    }

    private sealed record LoginResponse([property: JsonPropertyName("token")] string? Token);

    private sealed record CreateOrderResponse(
        [property: JsonPropertyName("order_id")] long OrderId,
        [property: JsonPropertyName("shipment_id")] long ShipmentId);

    private sealed record AssignAwbResponse([property: JsonPropertyName("response")] AssignAwbBody? Response);

    private sealed record AssignAwbBody([property: JsonPropertyName("data")] AssignAwbData? Data);

    private sealed record AssignAwbData(
        [property: JsonPropertyName("awb_code")] string? AwbCode,
        [property: JsonPropertyName("courier_name")] string? CourierName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shiprocket sign-in failed: HTTP {Status}")]
    private static partial void LogSignInFailed(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shiprocket {Path} failed: HTTP {Status} {Body}")]
    private static partial void LogCallFailed(ILogger logger, string path, int status, string body);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not reach Shiprocket for {Path}")]
    private static partial void LogCallError(ILogger logger, Exception exception, string path);
}

/// <summary>
/// Shiprocket's sign-in token, shared across requests. A singleton so a busy server signs in once
/// every ten days rather than once per booking; refreshed on demand when Shiprocket rejects it.
/// </summary>
internal sealed class ShiprocketTokenCache : IDisposable
{
    /// <summary>Shiprocket's tokens last ten days; renewing a day early avoids using one as it lapses.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(9);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTime _expiresAtUtc;

    public async Task<string?> GetAsync(
        Func<CancellationToken, Task<string?>> signIn,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh && _token is not null && DateTime.UtcNow < _expiresAtUtc)
        {
            return _token;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!forceRefresh && _token is not null && DateTime.UtcNow < _expiresAtUtc)
            {
                return _token;
            }

            _token = await signIn(cancellationToken);
            _expiresAtUtc = DateTime.UtcNow + Lifetime;

            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
