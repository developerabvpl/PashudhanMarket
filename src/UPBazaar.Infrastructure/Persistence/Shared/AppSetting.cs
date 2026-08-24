namespace UPBazaar.Infrastructure.Persistence.Shared;

/// <summary>
/// A runtime-editable setting. Anything that must change without a redeploy lives here;
/// anything secret does not - secrets come from environment or user-secrets.
/// </summary>
public sealed class AppSetting
{
    public long Id { get; init; }

    /// <summary>Dotted key, for example <c>orders.checkout.shipping_fee</c>.</summary>
    public required string Key { get; init; }

    public required string Value { get; set; }

    /// <summary>Owning module, or <c>shared</c> for platform-wide settings.</summary>
    public required string Module { get; init; }

    public string? Description { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public string? UpdatedBy { get; set; }
}
