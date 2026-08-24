using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Infrastructure.Time;

/// <inheritdoc />
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;
}
