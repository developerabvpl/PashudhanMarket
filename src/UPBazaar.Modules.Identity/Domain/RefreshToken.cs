using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Identity.Domain;

/// <summary>Why a refresh token stopped being usable.</summary>
public enum RefreshTokenRevocationReason
{
    /// <summary>Exchanged for a successor during normal rotation.</summary>
    Rotated = 0,

    /// <summary>The user signed out.</summary>
    SignedOut = 1,

    /// <summary>An already-rotated token was presented again; the whole family was revoked.</summary>
    ReuseDetected = 2,

    /// <summary>Revoked administratively, for example when an account is suspended.</summary>
    Administrative = 3,

    /// <summary>The password changed, so every existing session ends.</summary>
    PasswordChanged = 4,
}

/// <summary>
/// One issued refresh token.
///
/// Only the hash is stored, so a database leak does not hand over live sessions. Tokens
/// rotate: presenting one revokes it and issues a successor, and the chain is kept so that a
/// second presentation of an already-rotated token can be recognised as reuse rather than
/// silently accepted.
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken()
    {
    }

    public long UserId { get; private set; }

    /// <summary>SHA-256 of the token. The token itself is never persisted.</summary>
    public string TokenHash { get; private set; } = null!;

    /// <summary>
    /// Groups every token descended from one sign-in. Reuse revokes the family, ending that
    /// session everywhere without disturbing the user's other devices.
    /// </summary>
    public Guid FamilyId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public string? CreatedByIp { get; private set; }

    /// <summary>User agent, trimmed. Shown on a "where you are signed in" screen.</summary>
    public string? DeviceInfo { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public string? RevokedByIp { get; private set; }

    public RefreshTokenRevocationReason? RevocationReason { get; private set; }

    /// <summary>Hash of the token issued in its place, when rotated.</summary>
    public string? ReplacedByTokenHash { get; private set; }

    public bool IsRevoked => RevokedAtUtc is not null;

    public bool IsExpired(DateTime utcNow) => ExpiresAtUtc <= utcNow;

    public bool IsActive(DateTime utcNow) => !IsRevoked && !IsExpired(utcNow);

    public static RefreshToken Issue(
        long userId,
        string tokenHash,
        Guid familyId,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        string? ipAddress,
        string? deviceInfo) => new()
        {
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            CreatedByIp = ipAddress,
            DeviceInfo = Trim(deviceInfo),
        };

    public void Revoke(
        DateTime utcNow,
        RefreshTokenRevocationReason reason,
        string? ipAddress = null,
        string? replacedByTokenHash = null)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAtUtc = utcNow;
        RevocationReason = reason;
        RevokedByIp = ipAddress;
        ReplacedByTokenHash = replacedByTokenHash;
    }

    private static string? Trim(string? deviceInfo) =>
        deviceInfo?[..Math.Min(deviceInfo.Length, 256)];
}
