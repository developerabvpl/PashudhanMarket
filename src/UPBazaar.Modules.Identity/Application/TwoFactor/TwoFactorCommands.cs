using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.Modules.Identity.Services;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Application.TwoFactor;

/// <summary>Begins TOTP enrolment for a staff account.</summary>
public sealed record SetupTotpCommand(Guid UserId) : ICommand<TotpSetupDto>;

internal sealed class SetupTotpCommandValidator : AbstractValidator<SetupTotpCommand>
{
    public SetupTotpCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}

/// <summary>
/// Generates a secret and returns it with an otpauth URI.
///
/// The secret is stored but 2FA stays off until the user proves they can generate a code from
/// it. Enabling on setup would lock out anyone whose authenticator failed to save the entry.
/// </summary>
internal sealed class SetupTotpCommandHandler(
    UPBazaarDbContext dbContext,
    TotpService totp,
    IOptions<IdentityModuleOptions> options) : ICommandHandler<SetupTotpCommand, TotpSetupDto>
{
    public async Task<Result<TotpSetupDto>> HandleAsync(
        SetupTotpCommand command,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Set<User>()
            .FirstOrDefaultAsync(u => u.PublicId == command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<TotpSetupDto>(IdentityErrors.UserNotFound);
        }

        if (user.UserType != UserType.Staff)
        {
            return Result.Failure<TotpSetupDto>(IdentityErrors.TwoFactorStaffOnly);
        }

        var secret = totp.GenerateSecret();
        user.BeginTwoFactorSetup(secret);

        await dbContext.SaveChangesAsync(cancellationToken);

        var uri = TotpService.BuildAuthenticatorUri(
            options.Value.TotpIssuer,
            user.Email ?? user.DisplayName,
            secret);

        return new TotpSetupDto(secret, uri);
    }
}

/// <summary>Confirms enrolment by presenting a generated code.</summary>
public sealed record ConfirmTotpCommand(Guid UserId, string Code) : ICommand;

internal sealed class ConfirmTotpCommandValidator : AbstractValidator<ConfirmTotpCommand>
{
    public ConfirmTotpCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$");
    }
}

internal sealed class ConfirmTotpCommandHandler(
    UPBazaarDbContext dbContext,
    TotpService totp,
    IClock clock) : ICommandHandler<ConfirmTotpCommand>
{
    public async Task<Result> HandleAsync(ConfirmTotpCommand command, CancellationToken cancellationToken)
    {
        var user = await dbContext.Set<User>()
            .FirstOrDefaultAsync(u => u.PublicId == command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        if (user.TwoFactorSecret is null)
        {
            return Result.Failure(IdentityErrors.TwoFactorNotSetUp);
        }

        if (!totp.VerifyCode(user.TwoFactorSecret, command.Code, clock.UtcNow))
        {
            return Result.Failure(IdentityErrors.TwoFactorInvalid);
        }

        var confirmed = user.ConfirmTwoFactor();

        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Completes a sign-in that stopped at the second factor.</summary>
public sealed record VerifyTwoFactorCommand(
    string TwoFactorToken,
    string Code,
    RequestOrigin Origin) : ICommand<AuthTokensDto>;

internal sealed class VerifyTwoFactorCommandValidator : AbstractValidator<VerifyTwoFactorCommand>
{
    public VerifyTwoFactorCommandValidator()
    {
        RuleFor(x => x.TwoFactorToken).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$");
    }
}

internal sealed class VerifyTwoFactorCommandHandler(
    UPBazaarDbContext dbContext,
    TokenService tokenService,
    TotpService totp,
    LoginAuditWriter auditWriter,
    IClock clock) : ICommandHandler<VerifyTwoFactorCommand, AuthTokensDto>
{
    public async Task<Result<AuthTokensDto>> HandleAsync(VerifyTwoFactorCommand command, CancellationToken cancellationToken)
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
            return Result.Failure<AuthTokensDto>(IdentityErrors.TwoFactorInvalid);
        }
    }

    private async Task<Result<AuthTokensDto>> HandleCoreAsync(
        VerifyTwoFactorCommand command,
        CancellationToken cancellationToken)
    {
        var userId = await tokenService.ReadTwoFactorTicketAsync(command.TwoFactorToken);

        if (userId is null)
        {
            return Result.Failure<AuthTokensDto>(IdentityErrors.TwoFactorRequired);
        }

        var now = clock.UtcNow;

        var user = await dbContext.Set<User>()
            .Include(u => u.Roles)
            .ThenInclude(r => r.Role)
            .ThenInclude(r => r.Permissions)
            .ThenInclude(p => p.Permission)
            .FirstOrDefaultAsync(u => u.PublicId == userId.Value, cancellationToken);

        if (user is null || user.TwoFactorSecret is null || !user.TwoFactorEnabled)
        {
            return Result.Failure<AuthTokensDto>(IdentityErrors.TwoFactorNotSetUp);
        }

        if (user.IsLockedOut(now))
        {
            return Result.Failure<AuthTokensDto>(IdentityErrors.AccountLockedOut);
        }

        if (!totp.VerifyCode(user.TwoFactorSecret, command.Code, now))
        {
            // Counts towards lockout: without it the second factor could be brute-forced even
            // though the password could not.
            user.RegisterFailedAccess(now);

            auditWriter.RecordFailure(
                user.Id,
                user.Email,
                LoginMethod.TwoFactor,
                LoginFailureReason.InvalidTwoFactorCode,
                command.Origin);

            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthTokensDto>(IdentityErrors.TwoFactorInvalid);
        }

        user.RegisterSuccessfulAccess(now);

        var tokens = await tokenService.IssueAsync(user, command.Origin, cancellationToken);

        auditWriter.RecordSuccess(user.Id, LoginMethod.TwoFactor, command.Origin);

        await dbContext.SaveChangesAsync(cancellationToken);

        return tokens;
    }
}
