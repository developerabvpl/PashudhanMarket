using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Identity.Domain;

/// <summary>How the caller tried to authenticate.</summary>
public enum LoginMethod
{
    Password = 0,
    Otp = 1,
    RefreshToken = 2,
    TwoFactor = 3,
}

/// <summary>Why an attempt failed. Recorded for operators; never returned to the caller.</summary>
public enum LoginFailureReason
{
    None = 0,
    UnknownUser = 1,
    WrongPassword = 2,
    LockedOut = 3,
    Suspended = 4,
    InvalidOtp = 5,
    InvalidRefreshToken = 6,
    RefreshTokenReuse = 7,
    InvalidTwoFactorCode = 8,
}

/// <summary>
/// One authentication attempt, successful or not.
///
/// Separate from the platform audit log because it records attempts by people who may not have
/// an account at all, and because "who tried to get in" is a question asked far more often
/// than "who changed this row".
/// </summary>
public sealed class LoginAudit : Entity
{
    private LoginAudit()
    {
    }

    /// <summary>Null when the attempt named an account that does not exist.</summary>
    public long? UserId { get; private set; }

    /// <summary>
    /// What the caller typed: an email or mobile. Kept even for unknown users, because a burst
    /// of attempts against non-existent accounts is exactly what enumeration looks like.
    /// </summary>
    public string? AttemptedIdentifier { get; private set; }

    public LoginMethod Method { get; private set; }

    public bool Succeeded { get; private set; }

    public LoginFailureReason FailureReason { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string? CorrelationId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public static LoginAudit Success(
        long userId,
        LoginMethod method,
        DateTime utcNow,
        string? ipAddress,
        string? userAgent,
        string? correlationId) => new()
        {
            UserId = userId,
            Method = method,
            Succeeded = true,
            FailureReason = LoginFailureReason.None,
            IpAddress = ipAddress,
            UserAgent = Trim(userAgent),
            CorrelationId = correlationId,
            OccurredAtUtc = utcNow,
        };

    public static LoginAudit Failure(
        long? userId,
        string? attemptedIdentifier,
        LoginMethod method,
        LoginFailureReason reason,
        DateTime utcNow,
        string? ipAddress,
        string? userAgent,
        string? correlationId) => new()
        {
            UserId = userId,
            AttemptedIdentifier = Trim(attemptedIdentifier, 256),
            Method = method,
            Succeeded = false,
            FailureReason = reason,
            IpAddress = ipAddress,
            UserAgent = Trim(userAgent),
            CorrelationId = correlationId,
            OccurredAtUtc = utcNow,
        };

    private static string? Trim(string? value, int max = 512) =>
        value?[..Math.Min(value.Length, max)];
}
