namespace UPBazaar.SharedKernel.Abstractions;

/// <summary>Injectable clock. Everything is UTC; never call <c>DateTime.Now</c>.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
