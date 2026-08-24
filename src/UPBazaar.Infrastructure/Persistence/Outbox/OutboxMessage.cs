namespace UPBazaar.Infrastructure.Persistence.Outbox;

/// <summary>
/// A domain event persisted in the same transaction as the state change that raised it.
/// The Hangfire outbox processor picks these up and dispatches them to handlers, which is
/// how effects reach other modules without a direct call.
/// </summary>
public sealed class OutboxMessage
{
    public long Id { get; init; }

    public Guid EventId { get; init; }

    /// <summary>Assembly-qualified CLR type name used to rehydrate the payload.</summary>
    public required string Type { get; init; }

    public required string Payload { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    public DateTime? ProcessedAtUtc { get; set; }

    public int Attempts { get; set; }

    public string? Error { get; set; }
}
