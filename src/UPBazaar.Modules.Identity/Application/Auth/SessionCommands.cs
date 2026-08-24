using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.Modules.Identity.Services;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Application.Auth;

/// <summary>Exchanges a refresh token for a new pair.</summary>
public sealed record RefreshTokensCommand(string RefreshToken, RequestOrigin Origin)
    : ICommand<AuthTokensDto>;

internal sealed class RefreshTokensCommandValidator : AbstractValidator<RefreshTokensCommand>
{
    public RefreshTokensCommandValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
}

internal sealed class RefreshTokensCommandHandler(
    UPBazaarDbContext dbContext,
    TokenService tokenService,
    LoginAuditWriter auditWriter) : ICommandHandler<RefreshTokensCommand, AuthTokensDto>
{
    public async Task<Result<AuthTokensDto>> HandleAsync(
        RefreshTokensCommand command,
        CancellationToken cancellationToken)
    {
        var result = await tokenService.RotateAsync(command.RefreshToken, command.Origin, cancellationToken);

        if (result.IsFailure)
        {
            var reason = result.Error == IdentityErrors.RefreshTokenReuse
                ? LoginFailureReason.RefreshTokenReuse
                : LoginFailureReason.InvalidRefreshToken;

            auditWriter.RecordFailure(
                null, null, LoginMethod.RefreshToken, reason, command.Origin);
        }

        // Saved either way: a rotation writes the successor token, and a failure writes the
        // audit row and any family revocation the token service performed.
        await dbContext.SaveChangesAsync(cancellationToken);

        return result;
    }
}

/// <summary>Ends one session.</summary>
public sealed record LogoutCommand(string RefreshToken, RequestOrigin Origin) : ICommand;

internal sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
}

internal sealed class LogoutCommandHandler(
    UPBazaarDbContext dbContext,
    TokenService tokenService) : ICommandHandler<LogoutCommand>
{
    public async Task<Result> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        var result = await tokenService.RevokeAsync(command.RefreshToken, command.Origin, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return result;
    }
}

/// <summary>Changes the signed-in user's password.</summary>
public sealed record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword) : ICommand;

internal sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12).MaximumLength(256)
            .WithMessage("Choose a password of at least 12 characters.");
        RuleFor(x => x.NewPassword).NotEqual(x => x.CurrentPassword)
            .WithMessage("The new password must be different from the current one.");
    }
}

/// <summary>
/// Changes a password and ends every other session.
///
/// Revoking sessions is the point of a password change as often as not: someone who thinks
/// their account is compromised expects the intruder to be signed out, not merely inconvenienced.
/// </summary>
internal sealed class ChangePasswordCommandHandler(
    UPBazaarDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    TokenService tokenService) : ICommandHandler<ChangePasswordCommand>
{
    public async Task<Result> HandleAsync(
        ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Set<User>()
            .FirstOrDefaultAsync(u => u.PublicId == command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        if (user.PasswordHash is null)
        {
            return Result.Failure(IdentityErrors.PasswordNotSet);
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user, user.PasswordHash, command.CurrentPassword);

        if (verification == PasswordVerificationResult.Failed)
        {
            return Result.Failure(IdentityErrors.InvalidCredentials);
        }

        user.SetPassword(passwordHasher.HashPassword(user, command.NewPassword));

        await tokenService.RevokeAllForUserAsync(
            user.Id, RefreshTokenRevocationReason.PasswordChanged, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
