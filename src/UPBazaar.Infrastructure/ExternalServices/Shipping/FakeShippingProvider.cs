using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Infrastructure.ExternalServices.Shipping;

/// <summary>Sandbox logistics provider: deterministic AWB numbers, no network.</summary>
public sealed class FakeShippingProvider(IClock clock) : IShippingProvider
{
    private readonly ConcurrentDictionary<string, ShipmentBooking> _bookings = new();
    private readonly ConcurrentDictionary<string, string> _statuses = new();

    public string Provider => "fake";

    public Task<ShipmentBooking> CreateShipmentAsync(
        ShipmentBookingRequest request,
        CancellationToken cancellationToken)
    {
        var booking = _bookings.GetOrAdd(
            request.IdempotencyKey,
            _ => new ShipmentBooking(
                $"AWB{Deterministic(request.IdempotencyKey)}",
                "sandbox-express",
                clock.UtcNow.AddDays(4)));

        _statuses[booking.AwbNumber] = "booked";

        return Task.FromResult(booking);
    }

    public Task<ShipmentTracking> TrackAsync(string awbNumber, CancellationToken cancellationToken) =>
        Task.FromResult(new ShipmentTracking(
            awbNumber,
            _statuses.GetValueOrDefault(awbNumber, "unknown"),
            clock.UtcNow));

    public Task CancelAsync(string awbNumber, CancellationToken cancellationToken)
    {
        _statuses[awbNumber] = "cancelled";
        return Task.CompletedTask;
    }

    private static string Deterministic(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12];
}
