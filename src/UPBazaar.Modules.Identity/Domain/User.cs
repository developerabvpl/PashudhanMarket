using UPBazaar.Modules.Identity.Contracts.Events;
using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Domain;

/// <summary>Kind of account. Determines which sign-in methods apply.</summary>
public enum UserType
{
    /// <summary>Shops on the storefront. Signs in with a mobile OTP, optionally email.</summary>
    Buyer = 0,

    /// <summary>Runs a shop. Signs in with email and password.</summary>
    Seller = 1,

    /// <summary>Works for the marketplace. Password plus optional TOTP.</summary>
    Staff = 2,
}

/// <summary>Whether the account may sign in at all.</summary>
public enum UserStatus
{
    Active = 0,

    /// <summary>Blocked by an administrator. Reversible.</summary>
    Suspended = 1,

    /// <summary>Closed. Retained for audit and order history, never reactivated.</summary>
    Deactivated = 2,
}

/// <summary>
/// An account.
///
/// Aggregate root for everything sign-in related: roles held, refresh tokens issued, lockout
/// state. Buyers may exist with only a verified mobile and no password at all, which is why
/// <see cref="PasswordHash"/> and <see cref="Email"/> are both nullable.
/// </summary>
public sealed class User : AggregateRoot, IAuditable
{
    /// <summary>Failed attempts tolerated before the account locks.</summary>
    public const int MaxFailedAccessAttempts = 5;

    /// <summary>How long a lockout lasts once triggered.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly List<UserRole> _roles = [];

    private User()
    {
    }

    public UserType UserType { get; private set; }

    /// <summary>Normalised to lower case. Null for a buyer who only ever used a mobile.</summary>
    public string? Email { get; private set; }

    public bool EmailVerified { get; private set; }

    /// <summary>Ten digits, no country code. Null for staff who sign in by email.</summary>
    public string? Mobile { get; private set; }

    public bool MobileVerified { get; private set; }

    /// <summary>Null when the account has no password, such as an OTP-only buyer.</summary>
    public string? PasswordHash { get; private set; }

    public string DisplayName { get; private set; } = null!;

    /// <summary>ISO 639-1 code; <c>en</c> or <c>hi</c> today.</summary>
    public string PreferredLanguage { get; private set; } = "en";

    public UserStatus Status { get; private set; }

    /// <summary>Consecutive failures. Reset by any successful sign-in.</summary>
    public int AccessFailedCount { get; private set; }

    /// <summary>Set while the account is locked; null otherwise.</summary>
    public DateTime? LockoutEndUtc { get; private set; }

    public bool TwoFactorEnabled { get; private set; }

    /// <summary>Base32 TOTP secret. Present once setup has begun, even before confirmation.</summary>
    public string? TwoFactorSecret { get; private set; }

    public DateTime? LastLoginAtUtc { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>True while a lockout is in force at the given moment.</summary>
    public bool IsLockedOut(DateTime utcNow) => LockoutEndUtc is { } end && end > utcNow;

    /// <summary>Only an active account may sign in; suspended and closed accounts may not.</summary>
    public bool CanSignIn => Status == UserStatus.Active;

    public static User CreateBuyerWithMobile(string mobile, string displayName, string language)
    {
        var user = new User
        {
            UserType = UserType.Buyer,
            Mobile = mobile,
            // Only reachable by verifying an OTP sent to that number, so it is verified by
            // construction.
            MobileVerified = true,
            DisplayName = displayName,
            PreferredLanguage = language,
            Status = UserStatus.Active,
        };

        user.Raise(new UserRegisteredDomainEvent(
            user.PublicId,
            nameof(UserType.Buyer),
            null,
            mobile,
            displayName));

        return user;
    }

    public static User CreateWithPassword(
        UserType userType,
        string email,
        string passwordHash,
        string displayName,
        string language)
    {
        var user = new User
        {
            UserType = userType,
            Email = Normalize(email),
            PasswordHash = passwordHash,
            DisplayName = displayName,
            PreferredLanguage = language,
            Status = UserStatus.Active,
        };

        user.Raise(new UserRegisteredDomainEvent(
            user.PublicId,
            userType.ToString(),
            user.Email,
            null,
            displayName));

        return user;
    }

    /// <summary>Records a failed attempt and locks the account once the threshold is reached.</summary>
    public void RegisterFailedAccess(DateTime utcNow)
    {
        AccessFailedCount++;

        if (AccessFailedCount < MaxFailedAccessAttempts)
        {
            return;
        }

        LockoutEndUtc = utcNow.Add(LockoutDuration);
        AccessFailedCount = 0;

        Raise(new UserLockedOutDomainEvent(PublicId, LockoutEndUtc.Value));
    }

    /// <summary>Clears failure state and stamps the sign-in.</summary>
    public void RegisterSuccessfulAccess(DateTime utcNow)
    {
        AccessFailedCount = 0;
        LockoutEndUtc = null;
        LastLoginAtUtc = utcNow;
    }

    public Result SetPassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return Result.Failure(IdentityErrors.PasswordHashMissing);
        }

        PasswordHash = passwordHash;

        // A password change invalidates a lockout earned by guessing the old one.
        AccessFailedCount = 0;
        LockoutEndUtc = null;

        return Result.Success();
    }

    public void SetEmail(string email, bool verified)
    {
        Email = Normalize(email);
        EmailVerified = verified;
    }

    public void ConfirmEmail() => EmailVerified = true;

    public void SetMobile(string mobile, bool verified)
    {
        Mobile = mobile;
        MobileVerified = verified;
    }

    public void UpdateProfile(string displayName, string preferredLanguage)
    {
        DisplayName = displayName;
        PreferredLanguage = preferredLanguage;
    }

    public void SetStatus(UserStatus status) => Status = status;

    /// <summary>Stores a secret without enabling 2FA; enrolment is confirmed separately.</summary>
    public void BeginTwoFactorSetup(string base32Secret)
    {
        TwoFactorSecret = base32Secret;
        TwoFactorEnabled = false;
    }

    /// <summary>
    /// Turns 2FA on. Only valid once a secret exists and the user has proved they can generate
    /// a code from it, otherwise enrolment could lock someone out of their own account.
    /// </summary>
    public Result ConfirmTwoFactor()
    {
        if (string.IsNullOrWhiteSpace(TwoFactorSecret))
        {
            return Result.Failure(IdentityErrors.TwoFactorNotSetUp);
        }

        TwoFactorEnabled = true;

        return Result.Success();
    }

    public void DisableTwoFactor()
    {
        TwoFactorEnabled = false;
        TwoFactorSecret = null;
    }

    /// <summary>
    /// Records that a rotated refresh token was presented again. Raised on the user rather
    /// than the token so the event carries who was affected, which is what a security alert
    /// needs to name.
    /// </summary>
    public void RaiseReuseDetected(DateTime detectedAtUtc, string? ipAddress) =>
        Raise(new RefreshTokenReuseDetectedDomainEvent(PublicId, detectedAtUtc, ipAddress));

    public void AssignRole(Role role, DateTime utcNow, string? assignedBy)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (_roles.Any(r => r.RoleId == role.Id))
        {
            return;
        }

        _roles.Add(UserRole.Create(role, utcNow, assignedBy));
    }

    public void RemoveRole(long roleId)
    {
        var existing = _roles.FirstOrDefault(r => r.RoleId == roleId);

        if (existing is not null)
        {
            _roles.Remove(existing);
        }
    }

    public void ClearRoles() => _roles.Clear();

    /// <summary>Lower-cased so lookups are case-insensitive without a collation dependency.</summary>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
