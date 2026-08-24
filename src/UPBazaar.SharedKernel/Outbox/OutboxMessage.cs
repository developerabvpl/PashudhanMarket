namespace UPBazaar.SharedKernel.Outbox;

/// <summary>
/// A domain event persisted in the same transaction as the state change that raised it.
///
/// This is what makes cross-module effects reliable: the event cannot be published for a
/// change that rolled back, and it cannot be lost by a process that died before publishing.
/// The processor picks rows up afterwards and dispatches them.
/// </summary>
public sealed class OutboxMessage
{
    public long Id { get; init; }

    /// <summary>The event's own identity, unique-indexed so a retry cannot enqueue it twice.</summary>
    public Guid EventId { get; init; }

    /// <summary>
    /// CLR type name plus assembly name, without a version, so rebuilding the solution does
    /// not orphan messages already sitting in the table.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>JSON body of the event.</summary>
    public required string Payload { get; init; }

    /// <summary>Module that raised the event, for diagnostics and per-module dashboards.</summary>
    public string? Module { get; init; }

    /// <summary>Correlation id of the request that caused this event.</summary>
    public string? CorrelationId { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    /// <summary>Null until a handler run completes successfully.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>Incremented on every attempt, successful or not, so poison messages stop.</summary>
    public int Attempts { get; set; }

    /// <summary>Message from the last failed attempt; cleared once the message succeeds.</summary>
    public string? Error { get; set; }
}
