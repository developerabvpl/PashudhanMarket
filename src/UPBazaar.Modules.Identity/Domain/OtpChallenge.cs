using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Domain;

/// <summary>What a one-time code is being used for.</summary>
public enum OtpPurpose
{
    /// <summary>Buyer sign-in by mobile.</summary>
    Login = 0,

    /// <summary>Proving control of a mobile number already attached to an account.</summary>
    MobileVerification = 1,

    /// <summary>Resetting a forgotten password, delivered by email.</summary>
    PasswordReset = 2,
}

/// <summary>
/// A one-time code issued to a mobile number or email address.
///
/// Only the hash is stored: an operator reading the table must not be able to complete
/// somebody's sign-in. Attempts are counted so a six-digit code cannot be brute-forced within
/// its lifetime, and consumption is recorded so a code works exactly once.
/// </summary>
public sealed class OtpChallenge : Entity
{
    /// <summary>Wrong guesses tolerated before the challenge is dead.</summary>
    public const int MaxAttempts = 5;

    private OtpChallenge()
    {
    }

    public OtpPurpose Purpose { get; private set; }

    /// <summary>Mobile number or email address the code went to.</summary>
    public string Target { get; private set; } = null!;

    /// <summary>SHA-256 of the code, salted with the target so hashes are not interchangeable.</summary>
    public string CodeHash { get; private set; } = null!;

    public int Attempts { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    /// <summary>Requesting IP, used for rate limiting and for spotting enumeration.</summary>
    public string? RequestedByIp { get; private set; }

    public bool IsConsumed => ConsumedAtUtc is not null;

    public bool IsExpired(DateTime utcNow) => ExpiresAtUtc <= utcNow;

    public bool IsUsable(DateTime utcNow) => !IsConsumed && !IsExpired(utcNow) && Attempts < MaxAttempts;

    public static OtpChallenge Issue(
        OtpPurpose purpose,
        string target,
        string codeHash,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        string? requestedByIp) => new()
        {
            Purpose = purpose,
            Target = target,
            CodeHash = codeHash,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            RequestedByIp = requestedByIp,
        };

    /// <summary>
    /// Checks a presented code. Counts the attempt either way, so a wrong guess costs the
    /// caller one of its five chances whatever else happens.
    /// </summary>
    public Result Verify(string presentedCodeHash, DateTime utcNow)
    {
        if (IsConsumed)
        {
            return Result.Failure(IdentityErrors.OtpAlreadyUsed);
        }

        if (IsExpired(utcNow))
        {
            return Result.Failure(IdentityErrors.OtpExpired);
        }

        if (Attempts >= MaxAttempts)
        {
            return Result.Failure(IdentityErrors.OtpTooManyAttempts);
        }

        Attempts++;

        if (!string.Equals(CodeHash, presentedCodeHash, StringComparison.Ordinal))
        {
            return Result.Failure(IdentityErrors.OtpInvalid);
        }

        ConsumedAtUtc = utcNow;

        return Result.Success();
    }
}
