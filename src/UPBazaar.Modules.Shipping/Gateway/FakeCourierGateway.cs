using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Gateway;

/// <summary>
/// Shiprocket's stand-in for development and tests. Books instantly with made-up ids, and accepts
/// tracking webhooks carrying <see cref="WebhookToken"/>, which is public - hence Development and
/// Testing only.
///
/// A pickup location whose name contains <see cref="FailingPickupMarker"/> makes AWB assignment
/// fail, so tests can check that packing again resumes a half-finished booking.
/// </summary>
public sealed class FakeCourierGateway : ICourierGateway
{
    public const string GatewayName = "Fake";

    public const string WebhookToken = "fake_webhook_token";

    public const string FailingPickupMarker = "fail-awb";

    /// <summary>The only courier the fake quotes for.</summary>
    public const string FakeCourierId = "1";

    private readonly HashSet<string> _failingShipments = [];

    public string Name => GatewayName;

    public bool IsEnabled => true;

    public Task<Result<CarrierOrder>> CreateOrderAsync(CourierOrderRequest request, CancellationToken cancellationToken)
    {
        var shipmentId = Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (request.PickupLocation.Contains(FailingPickupMarker, StringComparison.OrdinalIgnoreCase))
        {
            lock (_failingShipments)
            {
                _failingShipments.Add(shipmentId);
            }
        }

        return Task.FromResult(Result.Success(new CarrierOrder($"9{shipmentId}", shipmentId)));
    }

    /// <summary>
    /// A simple, predictable price list, so tests can say what a parcel should cost: Rs 40 for the
    /// first 500 g and Rs 20 for each 500 g after, plus Rs 30 or 2% of the value (whichever is more)
    /// to collect cash on delivery.
    /// </summary>
    public Task<Result<CourierQuote>> QuoteAsync(CourierQuoteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var halfKilos = Math.Max(1, (request.WeightGrams + 499) / 500);
        var freight = 40m + (20m * (halfKilos - 1));
        var cod = request.CashOnDelivery ? Math.Max(30m, Math.Round(request.DeclaredValue * 0.02m, 2)) : 0m;

        return Task.FromResult(Result.Success(new CourierQuote(FakeCourierId, "Fake Express", freight, cod)));
    }

    public Task<Result<CarrierAwb>> AssignAwbAsync(string carrierShipmentId, string? courierId, CancellationToken cancellationToken)
    {
        bool fails;

        lock (_failingShipments)
        {
            // Fails once, then succeeds, the way a courier that was briefly unserviceable would.
            fails = _failingShipments.Remove(carrierShipmentId);
        }

        return Task.FromResult(fails
            ? Result.Failure<CarrierAwb>(ShippingErrors.CourierUnavailable)
            : Result.Success(new CarrierAwb($"FAKE{carrierShipmentId}", "Fake Express")));
    }

    public Task<Result> RequestPickupAsync(string carrierShipmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success());

    public Task<Result<CarrierOrder>> CreateReturnOrderAsync(CourierReturnRequest request, CancellationToken cancellationToken)
    {
        var shipmentId = Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return Task.FromResult(Result.Success(new CarrierOrder($"8{shipmentId}", shipmentId)));
    }

    public Task<Result<CarrierAwb>> AssignReturnAwbAsync(string carrierShipmentId, string? courierId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new CarrierAwb($"FAKER{carrierShipmentId}", "Fake Express Reverse")));

    public Task<Result> CancelAsync(string carrierOrderId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success());

    public bool IsWebhookTokenValid(string? token) => token == WebhookToken;

    public string TrackingUrl(string awb) => $"https://example.invalid/track/{Uri.EscapeDataString(awb)}";
}

/// <summary>What a server without Shiprocket credentials gets: courier booking off.</summary>
internal sealed class UnconfiguredCourierGateway : ICourierGateway
{
    public string Name => "None";

    public bool IsEnabled => false;

    public Task<Result<CarrierOrder>> CreateOrderAsync(CourierOrderRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<CarrierOrder>(ShippingErrors.CourierDisabled));

    public Task<Result<CourierQuote>> QuoteAsync(CourierQuoteRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<CourierQuote>(ShippingErrors.CourierDisabled));

    public Task<Result<CarrierAwb>> AssignAwbAsync(string carrierShipmentId, string? courierId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<CarrierAwb>(ShippingErrors.CourierDisabled));

    public Task<Result> RequestPickupAsync(string carrierShipmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(ShippingErrors.CourierDisabled));

    public Task<Result<CarrierOrder>> CreateReturnOrderAsync(CourierReturnRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<CarrierOrder>(ShippingErrors.CourierDisabled));

    public Task<Result<CarrierAwb>> AssignReturnAwbAsync(string carrierShipmentId, string? courierId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<CarrierAwb>(ShippingErrors.CourierDisabled));

    public Task<Result> CancelAsync(string carrierOrderId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(ShippingErrors.CourierDisabled));

    public bool IsWebhookTokenValid(string? token) => false;

    public string TrackingUrl(string awb) => string.Empty;
}
