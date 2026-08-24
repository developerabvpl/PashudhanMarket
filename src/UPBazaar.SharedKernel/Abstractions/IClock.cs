namespace UPBazaar.SharedKernel.Abstractions;

/// <summary>Injectable clock. All timestamps are UTC; never call DateTime.Now.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
