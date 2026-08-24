namespace UPBazaar.SharedKernel.Abstractions;

/// <summary>
/// Backs the at-most-once guarantee for checkout, refunds and inbound webhooks.
/// A caller reserves a key before doing work, then records the response so that a retry
/// with the same key replays it instead of repeating the side effect.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Returns the stored response when this key was already processed, otherwise null
    /// after reserving the key for the current caller.
    /// </summary>
    Task<IdempotencyLookup> TryBeginAsync(
        string key,
        string endpoint,
        string requestHash,
        CancellationToken cancellationToken);

    /// <summary>Stores the response to replay on subsequent retries of the same key.</summary>
    Task CompleteAsync(string key, string responsePayload, CancellationToken cancellationToken);
}

/// <summary>
/// Result of reserving an idempotency key.
/// </summary>
/// <param name="IsReplay">True when the key was seen before and a response is available.</param>
/// <param name="ReplayPayload">Stored response body, when IsReplay is true.</param>
/// <param name="IsConflict">
/// True when the key was reused with a different request body, which is a client error.
/// </param>
public readonly record struct IdempotencyLookup(
    bool IsReplay,
    string? ReplayPayload,
    bool IsConflict)
{
    public static IdempotencyLookup Fresh() => new(false, null, false);

    public static IdempotencyLookup Replay(string? payload) => new(true, payload, false);

    public static IdempotencyLookup Conflict() => new(false, null, true);
}
