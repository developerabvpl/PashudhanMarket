using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
