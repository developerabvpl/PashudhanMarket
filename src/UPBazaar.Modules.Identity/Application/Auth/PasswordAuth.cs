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

/// <summary>Registers a buyer with an email and password.</summary>
public sealed record RegisterBuyerCommand(
    string Email,
    string Password,
    string DisplayName,
    string PreferredLanguage,
    RequestOrigin Origin) : ICommand<AuthTokensDto>;

internal sealed class RegisterBuyerCommandValidator : AbstractValidator<RegisterBuyerCommand>
{
    public RegisterBuyerCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(128);
        RuleFor(x => x.PreferredLanguage).NotEmpty().Must(l => l is "en" or "hi")
            .WithMessage("Preferred language must be 'en' or 'hi'.");

        // Length beats composition rules: a 12-character passphrase resists guessing better
        // than eight characters tortured into containing a symbol.
        RuleFor(x => x.Password).NotEmpty().MinimumLength(12).MaximumLength(256)
            .WithMessage("Choose a password of at least 12 characters.");
    }
}

internal sealed class RegisterBuyerCommandHandler(
    UPBazaarDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    TokenService tokenService,
    LoginAuditWriter auditWriter,
    IClock clock) : ICommandHandler<RegisterBuyerCommand, AuthTokensDto>
{
    public async Task<Result<AuthTokensDto>> HandleAsync(
        RegisterBuyerCommand command,
        CancellationToken cancellationToken)
    {
        var email = User.Normalize(command.Email);

        if (await dbContext.Set<User>().AnyAsync(u => u.Email == email, cancellationToken))
        {
            return Result.Failure<AuthTokensDto>(IdentityErrors.EmailAlreadyRegistered);
        }

        var user = User.CreateWithPassword(
            UserType.Buyer,
            email,
            passwordHash: string.Empty,
            command.DisplayName.Trim(),
            command.PreferredLanguage);

        user.SetPassword(passwordHasher.HashPassword(user, command.Password));

        var buyerRole = await dbContext.Set<Role>()
            .FirstOrDefaultAsync(r => r.Name == PermissionCatalog.RoleNames.Buyer, cancellationToken);

        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (buyerRole is not null)
        {
            user.AssignRole(buyerRole, clock.UtcNow, assignedBy: "self-registration");
        }

        user.RegisterSuccessfulAccess(clock.UtcNow);

        var tokens = await tokenService.IssueAsync(user, command.Origin, cancellationToken);

        auditWriter.RecordSuccess(user.Id, LoginMethod.Password, command.Origin);

        await dbContext.SaveChangesAsync(cancellationToken);

        return tokens;
    }
}

/// <summary>Signs in with email and password.</summary>
public sealed record LoginCommand(string Email, string Password, RequestOrigin Origin)
    : ICommand<AuthResultDto>;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

/// <summary>
/// Password sign-in, with lockout.
///
/// Every failure path returns the same error. Distinguishing "no such account" from "wrong
/// password" would let anyone test whether an address is registered, and distinguishing
/// "locked out" tells an attacker their guessing is working.
/// </summary>
internal sealed class LoginCommandHandler(
    UPBazaarDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    TokenService tokenService,
    LoginAuditWriter auditWriter,
    IClock clock) : ICommandHandler<LoginCommand, AuthResultDto>
{
    public async Task<Result<AuthResultDto>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
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
            return Result.Failure<AuthResultDto>(IdentityErrors.InvalidCredentials);
        }
    }

    private async Task<Result<AuthResultDto>> HandleCoreAsync(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        var email = User.Normalize(command.Email);
        var now = clock.UtcNow;

        var user = await dbContext.Set<User>()
            .Include(u => u.Roles)
            .ThenInclude(r => r.Role)
            .ThenInclude(r => r.Permissions)
            .ThenInclude(p => p.Permission)
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            auditWriter.RecordFailure(
                null, email, LoginMethod.Password, LoginFailureReason.UnknownUser, command.Origin);

            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthResultDto>(IdentityErrors.InvalidCredentials);
        }

        if (user.IsLockedOut(now))
        {
            auditWriter.RecordFailure(
                user.Id, email, LoginMethod.Password, LoginFailureReason.LockedOut, command.Origin);

            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthResultDto>(IdentityErrors.AccountLockedOut);
        }

        if (!user.CanSignIn)
        {
            auditWriter.RecordFailure(
                user.Id, email, LoginMethod.Password, LoginFailureReason.Suspended, command.Origin);

            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthResultDto>(IdentityErrors.AccountNotActive);
        }

        if (user.PasswordHash is null)
        {
            auditWriter.RecordFailure(
                user.Id, email, LoginMethod.Password, LoginFailureReason.WrongPassword, command.Origin);

            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthResultDto>(IdentityErrors.PasswordNotSet);
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, command.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.RegisterFailedAccess(now);

            auditWriter.RecordFailure(
                user.Id, email, LoginMethod.Password, LoginFailureReason.WrongPassword, command.Origin);

            await dbContext.SaveChangesAsync(cancellationToken);

            // Locking out on this very attempt still reports invalid credentials, so the
            // threshold cannot be probed.
            return Result.Failure<AuthResultDto>(IdentityErrors.InvalidCredentials);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // The hasher's work factor has increased since this password was set; upgrade it
            // now that the plaintext is briefly in hand.
            user.SetPassword(passwordHasher.HashPassword(user, command.Password));
        }

        if (user.TwoFactorEnabled)
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success(new AuthResultDto(
                null,
                RequiresTwoFactor: true,
                TwoFactorToken: tokenService.IssueTwoFactorTicket(user)));
        }

        user.RegisterSuccessfulAccess(now);

        var tokens = await tokenService.IssueAsync(user, command.Origin, cancellationToken);

        auditWriter.RecordSuccess(user.Id, LoginMethod.Password, command.Origin);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new AuthResultDto(tokens));
    }
}
