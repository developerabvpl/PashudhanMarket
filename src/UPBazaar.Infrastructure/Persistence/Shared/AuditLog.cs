namespace UPBazaar.Infrastructure.Persistence.Shared;

/// <summary>What kind of change produced an audit row.</summary>
public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
}

/// <summary>
/// Immutable record of a state-changing action. Written by <c>AuditInterceptor</c> for every
/// entity implementing <c>IAuditable</c>; nothing writes these by hand.
/// </summary>
public sealed class AuditLog
{
    public long Id { get; init; }

    /// <summary>Owning module, derived from the entity's SQL schema.</summary>
    public required string Module { get; init; }

    public required string EntityType { get; init; }

    /// <summary>PublicId of the affected entity, or empty for entities without one.</summary>
    public Guid EntityPublicId { get; init; }

    public AuditAction Action { get; init; }

    /// <summary>JSON object of changed properties, with sensitive values redacted.</summary>
    public string? Changes { get; init; }

    public string? UserId { get; init; }

    public string? UserName { get; init; }

    /// <summary>Ties the row back to the request that caused it, and to the log lines.</summary>
    public string? CorrelationId { get; init; }

    public DateTime OccurredAtUtc { get; init; }
}
