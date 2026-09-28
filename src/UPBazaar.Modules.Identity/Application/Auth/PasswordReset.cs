using System.Security.Cryptography;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.Modules.Identity.Services;
using UPBazaar.Modules.Notifications.Contracts;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Application.Auth;

/// <summary>Starts a password reset by emailing a single-use token.</summary>
public sealed record ForgotPasswordCommand(string Email, RequestOrigin Origin) : ICommand;

internal sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator() =>
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

/// <summary>
/// Issues a reset token.
///
/// Always reports success, whether or not the address is registered. Anything else turns the
/// forgotten-password form into a way to test which email addresses have accounts.
/// </summary>
internal sealed class ForgotPasswordCommandHandler(
    UPBazaarDbContext dbContext,
    IEmailSender emailSender,
    IOptions<IdentityModuleOptions> options,
    IClock clock) : ICommandHandler<ForgotPasswordCommand>
{
    /// <summary>Longer than an SMS code: it is clicked, not typed, so length costs nothing.</summary>
    private const int TokenBytes = 32;

    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result> HandleAsync(
        ForgotPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var email = User.Normalize(command.Email);
        var now = clock.UtcNow;

        var user = await dbContext.Set<User>()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            return Result.Success();
        }

        var token = Base64UrlToken();

        dbContext.Set<OtpChallenge>().Add(OtpChallenge.Issue(
            OtpPurpose.PasswordReset,
            email,
            OtpCodes.Hash(email, token),
            now,
            // An emailed link may sit unread for a while; an SMS code should not.
            now.AddMinutes(Math.Max(_options.OtpLifetimeMinutes, 30)),
            command.Origin.IpAddress));

        await dbContext.SaveChangesAsync(cancellationToken);

        var link = $"{_options.PasswordResetUrl}?email={Uri.EscapeDataString(email)}"
            + $"&token={Uri.EscapeDataString(token)}";

        await emailSender.SendAsync(
            new EmailMessage(
                email,
                "Reset your UP Bazaar password",
                $"""
                 <p>Someone asked to reset the password for this account.</p>
                 <p><a href="{link}">Choose a new password</a></p>
                 <p>The link stops working in 30 minutes. If this was not you, no action is needed.</p>
                 """),
            cancellationToken);

        return Result.Success();
    }

    private static string Base64UrlToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}

/// <summary>Completes a reset using the emailed token.</summary>
public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword) : ICommand;

internal sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Token).NotEmpty().MaximumLength(256);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12).MaximumLength(256)
            .WithMessage("Choose a password of at least 12 characters.");
    }
}

/// <summary>
/// Sets a new password against a valid reset token, then ends every existing session.
///
/// Not in the original endpoint list, but a forgotten-password flow that issues a token and
/// has nowhere to redeem it is not a feature. The two halves ship together.
/// </summary>
internal sealed class ResetPasswordCommandHandler(
    UPBazaarDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    TokenService tokenService,
    IClock clock) : ICommandHandler<ResetPasswordCommand>
{
    public async Task<Result> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await HandleCoreAsync(command, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another attempt at the same moment counted first. Answered as a wrong guess, whatever
            // this one was, so firing guesses in parallel neither dodges the attempt limit nor
            // reveals which guess was right.
            return Result.Failure(IdentityErrors.OtpInvalid);
        }
    }

    private async Task<Result> HandleCoreAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var email = User.Normalize(command.Email);
        var now = clock.UtcNow;

        var challenge = await dbContext.Set<OtpChallenge>()
            .Where(c => c.Target == email
                && c.Purpose == OtpPurpose.PasswordReset
                && c.ConsumedAtUtc == null)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (challenge is null)
        {
            return Result.Failure(IdentityErrors.OtpNotFound);
        }

        var verification = challenge.Verify(OtpCodes.Hash(email, command.Token), now);

        if (verification.IsFailure)
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            return verification;
        }

        var user = await dbContext.Set<User>()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        user.SetPassword(passwordHasher.HashPassword(user, command.NewPassword));
        user.ConfirmEmail();

        await tokenService.RevokeAllForUserAsync(
            user.Id, RefreshTokenRevocationReason.PasswordChanged, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
