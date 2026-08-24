using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Domain;

/// <summary>
/// Every failure this module can return.
///
/// Sign-in failures deliberately collapse to one code and one message. Telling a caller
/// whether the account exists, or whether it was the password that was wrong, turns the login
/// form into an account-enumeration oracle.
/// </summary>
public static class IdentityErrors
{
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        "identity.auth.invalid_credentials",
        "The email or password is incorrect.");

    public static readonly Error AccountLockedOut = Error.Forbidden(
        "identity.auth.locked_out",
        "Too many failed attempts. Try again in 15 minutes.");

    public static readonly Error AccountNotActive = Error.Forbidden(
        "identity.auth.account_not_active",
        "This account cannot sign in.");

    public static readonly Error EmailAlreadyRegistered = Error.Conflict(
        "identity.register.email_taken",
        "An account already exists for this email address.");

    public static readonly Error PasswordHashMissing = Error.Failure(
        "identity.password.hash_missing",
        "A password hash is required.");

    public static readonly Error PasswordNotSet = Error.Conflict(
        "identity.password.not_set",
        "This account has no password. Sign in with a one-time code instead.");

    public static readonly Error OtpInvalid = Error.Unauthorized(
        "identity.otp.invalid",
        "That code is not correct.");

    public static readonly Error OtpExpired = Error.Unauthorized(
        "identity.otp.expired",
        "That code has expired. Request a new one.");

    public static readonly Error OtpAlreadyUsed = Error.Unauthorized(
        "identity.otp.already_used",
        "That code has already been used.");

    public static readonly Error OtpTooManyAttempts = Error.Forbidden(
        "identity.otp.too_many_attempts",
        "Too many incorrect codes. Request a new one.");

    public static readonly Error OtpNotFound = Error.Unauthorized(
        "identity.otp.not_found",
        "No code is outstanding for this number. Request one first.");

    public static readonly Error OtpRateLimited = Error.Conflict(
        "identity.otp.rate_limited",
        "Too many codes requested. Wait a few minutes before trying again.");

    public static readonly Error RefreshTokenInvalid = Error.Unauthorized(
        "identity.refresh.invalid",
        "That refresh token is not valid.");

    public static readonly Error RefreshTokenReuse = Error.Unauthorized(
        "identity.refresh.reuse_detected",
        "This session has been ended for security reasons. Sign in again.");

    public static readonly Error TwoFactorRequired = Error.Unauthorized(
        "identity.2fa.required",
        "A two-factor code is required.");

    public static readonly Error TwoFactorInvalid = Error.Unauthorized(
        "identity.2fa.invalid_code",
        "That two-factor code is not correct.");

    public static readonly Error TwoFactorNotSetUp = Error.Conflict(
        "identity.2fa.not_set_up",
        "Two-factor authentication has not been set up for this account.");

    public static readonly Error TwoFactorStaffOnly = Error.Forbidden(
        "identity.2fa.staff_only",
        "Two-factor authentication is available to staff accounts only.");

    public static readonly Error UserNotFound = Error.NotFound(
        "identity.user.not_found",
        "The user does not exist.");

    public static readonly Error RoleNotFound = Error.NotFound(
        "identity.role.not_found",
        "The role does not exist.");

    public static readonly Error CannotModifySelf = Error.Conflict(
        "identity.user.cannot_modify_self",
        "You cannot change your own roles or status.");
}
