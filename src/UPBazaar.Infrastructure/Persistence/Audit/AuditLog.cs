namespace UPBazaar.Infrastructure.Persistence.Audit;

public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
}

/// <summary>
/// Immutable record of a state-changing action, written by the persistence interceptor for
/// every entity implementing IAuditable. Nothing writes these by hand.
/// </summary>
public sealed class AuditLog
{
    public long Id { get; init; }

    /// <summary>Owning module, derived from the entity SQL schema.</summary>
    public required string Module { get; init; }

    public required string EntityType { get; init; }

    /// <summary>PublicId of the affected entity.</summary>
    public Guid EntityPublicId { get; init; }

    public AuditAction Action { get; init; }

    /// <summary>JSON object of changed properties, with sensitive values redacted.</summary>
    public string? Changes { get; init; }

    public string? UserId { get; init; }

    public string? UserName { get; init; }

    /// <summary>Correlates the audit entry with the request log.</summary>
    public string? TraceId { get; init; }

    public DateTime OccurredAtUtc { get; init; }
}
