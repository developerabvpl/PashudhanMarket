namespace UPBazaar.Infrastructure.Persistence.Shared;

/// <summary>
/// An on/off switch for behaviour that is being rolled out.
///
/// <see cref="RolloutPercentage"/> supports gradual enablement; a flag is fully on at 100 and
/// fully off when <see cref="IsEnabled"/> is false, whatever the percentage says.
/// </summary>
public sealed class FeatureFlag
{
    public long Id { get; init; }

    /// <summary>Dotted key, for example <c>payments.upi_intent</c>.</summary>
    public required string Key { get; init; }

    public bool IsEnabled { get; set; }

    /// <summary>0-100. Ignored when <see cref="IsEnabled"/> is false.</summary>
    public int RolloutPercentage { get; set; }

    public required string Module { get; init; }

    public string? Description { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public string? UpdatedBy { get; set; }
}
