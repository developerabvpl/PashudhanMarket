namespace UPBazaar.Infrastructure.Api;

/// <summary>
/// Names of the rate-limiting policies endpoints opt into with <c>[EnableRateLimiting(...)]</c>.
/// The API host defines what each allows.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Endpoints that take a password, a one-time code or a reset token, or that send one: a cap
    /// per address on how fast anyone can guess, on top of each account's own attempt limits.
    /// </summary>
    public const string SignIn = "sign-in";
}
