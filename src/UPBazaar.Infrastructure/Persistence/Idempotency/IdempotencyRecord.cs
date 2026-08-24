namespace UPBazaar.Infrastructure.Persistence.Idempotency;

/// <summary>
/// One reserved idempotency key. The unique index on Key is what makes a concurrent retry
/// lose the race rather than duplicate the side effect.
/// </summary>
public sealed class IdempotencyRecord
{
    public long Id { get; init; }

    public required string Key { get; init; }

    /// <summary>Route the key was used against, so the same key cannot straddle two operations.</summary>
    public required string Endpoint { get; init; }

    /// <summary>SHA-256 of the request body; a mismatch on replay is a client error.</summary>
    public required string RequestHash { get; init; }

    public string? ResponsePayload { get; set; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? CompletedAtUtc { get; set; }
}
