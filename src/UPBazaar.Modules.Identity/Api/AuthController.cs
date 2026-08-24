using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.Infrastructure.Api;
using UPBazaar.Modules.Identity.Application.Auth;
using UPBazaar.Modules.Identity.Application.TwoFactor;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Identity.Services;
using UPBazaar.SharedKernel.Messaging;

namespace UPBazaar.Modules.Identity.Api;

/// <summary>Sign-in, sign-up and session management.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[Produces("application/json")]
public sealed class AuthController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Registers a buyer with an email address and password.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EndpointSummary("Register a buyer")]
    [EndpointDescription("Creates a buyer account from an email and password and signs them in.")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthTokensDto>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RegisterBuyerCommand(
            request.Email,
            request.Password,
            request.DisplayName,
            request.PreferredLanguage ?? "en",
            HttpContext.Origin());

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }

    /// <summary>Signs in with email and password.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EndpointSummary("Sign in with a password")]
    [EndpointDescription(
        "Returns tokens, or a two-factor challenge when the account has TOTP enabled. "
        + "Five consecutive failures lock the account for 15 minutes.")]
    [ProducesResponseType<AuthResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResultDto>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new LoginCommand(request.Email, request.Password, HttpContext.Origin());

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }

    /// <summary>Sends a one-time code to a mobile number.</summary>
    [HttpPost("request-otp")]
    [AllowAnonymous]
    [EndpointSummary("Request a sign-in code")]
    [EndpointDescription(
        "Sends a six-digit code by SMS. Rate limited per mobile number and per IP address. "
        + "The response is the same whether or not the number has an account.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> RequestOtp(
        RequestOtpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RequestOtpCommand(request.Mobile, HttpContext.Origin());

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }

    /// <summary>Exchanges a mobile code for tokens, creating the buyer on first use.</summary>
    [HttpPost("verify-otp")]
    [AllowAnonymous]
    [EndpointSummary("Verify a sign-in code")]
    [EndpointDescription(
        "Signs the buyer in. If the number has no account yet, one is created, because "
        + "possession of the number has just been proved.")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthTokensDto>> VerifyOtp(
        VerifyOtpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new VerifyOtpCommand(
            request.Mobile,
            request.Code,
            request.DisplayName,
            HttpContext.Origin());

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }

    /// <summary>Exchanges a refresh token for a new pair.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EndpointSummary("Refresh tokens")]
    [EndpointDescription(
        "Rotates the refresh token. Presenting one that was already rotated is treated as "
        + "theft: the whole session family is revoked and the caller must sign in again.")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokensDto>> Refresh(
        RefreshRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RefreshTokensCommand(request.RefreshToken, HttpContext.Origin());

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }

    /// <summary>Ends the session the refresh token belongs to.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [EndpointSummary("Sign out")]
    [EndpointDescription("Revokes the supplied refresh token. Succeeds even if it was already dead.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new LogoutCommand(request.RefreshToken, HttpContext.Origin());

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }

    /// <summary>Changes the signed-in user's password.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [EndpointSummary("Change password")]
    [EndpointDescription("Requires the current password, and ends every other session on success.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (HttpContext.CurrentUserId() is not { } userId)
        {
            return Unauthorized();
        }

        var command = new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword);

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }

    /// <summary>Starts a password reset.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EndpointSummary("Request a password reset")]
    [EndpointDescription(
        "Emails a single-use reset link. Always reports success, so the endpoint cannot be "
        + "used to discover which addresses have accounts.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new ForgotPasswordCommand(request.Email, HttpContext.Origin());

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }

    /// <summary>Completes a password reset with the emailed token.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EndpointSummary("Complete a password reset")]
    [EndpointDescription("Sets a new password using the token from the reset email.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new ResetPasswordCommand(request.Email, request.Token, request.NewPassword);

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }

    /// <summary>Completes a sign-in that stopped at the two-factor prompt.</summary>
    [HttpPost("verify-2fa")]
    [AllowAnonymous]
    [EndpointSummary("Verify a two-factor code")]
    [EndpointDescription("Exchanges the two-factor token from /auth/login plus a TOTP code for tokens.")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokensDto>> VerifyTwoFactor(
        VerifyTwoFactorRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new VerifyTwoFactorCommand(
            request.TwoFactorToken,
            request.Code,
            HttpContext.Origin());

        return (await dispatcher.SendAsync(command, cancellationToken)).ToActionResult();
    }
}

/// <param name="Email">Email address, used as the sign-in name.</param>
/// <param name="Password">At least 12 characters.</param>
/// <param name="DisplayName">Name shown across the storefront.</param>
/// <param name="PreferredLanguage">"en" or "hi". Defaults to "en".</param>
public sealed record RegisterRequest(
    string Email,
    string Password,
    string DisplayName,
    string? PreferredLanguage);

/// <param name="Email">Email address.</param>
/// <param name="Password">Password.</param>
public sealed record LoginRequest(string Email, string Password);

/// <param name="Mobile">Ten-digit Indian mobile number.</param>
public sealed record RequestOtpRequest(string Mobile);

/// <param name="Mobile">The number the code was sent to.</param>
/// <param name="Code">Six-digit code.</param>
/// <param name="DisplayName">Optional name, used when the account is created.</param>
public sealed record VerifyOtpRequest(string Mobile, string Code, string? DisplayName);

/// <param name="RefreshToken">The refresh token issued with the last access token.</param>
public sealed record RefreshRequest(string RefreshToken);

/// <param name="CurrentPassword">The password in force now.</param>
/// <param name="NewPassword">Replacement password, at least 12 characters.</param>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <param name="Email">Address to send the reset link to.</param>
public sealed record ForgotPasswordRequest(string Email);

/// <param name="Email">Address the reset was requested for.</param>
/// <param name="Token">Token from the reset email.</param>
/// <param name="NewPassword">Replacement password, at least 12 characters.</param>
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

/// <param name="TwoFactorToken">Token returned by /auth/login.</param>
/// <param name="Code">Six-digit code from the authenticator app.</param>
public sealed record VerifyTwoFactorRequest(string TwoFactorToken, string Code);
