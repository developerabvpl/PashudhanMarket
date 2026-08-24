using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.Modules.Identity.Services;
using UPBazaar.Modules.Notifications.Contracts;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Application.Auth;

/// <summary>Asks for a one-time code to be sent to a mobile number.</summary>
public sealed record RequestOtpCommand(string Mobile, RequestOrigin Origin) : ICommand;

internal sealed class RequestOtpCommandValidator : AbstractValidator<RequestOtpCommand>
{
    public RequestOtpCommandValidator() =>
        RuleFor(x => x.Mobile).NotEmpty().Matches("^[6-9][0-9]{9}$")
            .WithMessage("Enter a valid 10-digit Indian mobile number.");
}

/// <summary>
/// Issues a login code.
///
/// Rate limited on two axes. Per mobile, because otherwise anyone can use the endpoint to send
/// somebody unlimited texts at our expense. Per IP, because a single caller walking through a
/// range of numbers is how attackers find which ones are registered - and SMS costs money per
/// message either way.
///
/// The response is the same whether or not the number belongs to an account: verifying a code
/// creates the buyer if none exists, so there is nothing to disclose.
/// </summary>
internal sealed class RequestOtpCommandHandler(
    UPBazaarDbContext dbContext,
    ISmsSender smsSender,
    IOptions<IdentityModuleOptions> options,
    IClock clock) : ICommandHandler<RequestOtpCommand>
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result> HandleAsync(RequestOtpCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var windowStart = now.AddMinutes(-_options.OtpRateLimitWindowMinutes);
        var mobile = command.Mobile.Trim();

        var perMobile = await dbContext.Set<OtpChallenge>()
            .CountAsync(
                c => c.Target == mobile && c.Purpose == OtpPurpose.Login && c.CreatedAtUtc >= windowStart,
                cancellationToken);

        if (perMobile >= _options.OtpPerMobilePerWindow)
        {
            return Result.Failure(IdentityErrors.OtpRateLimited);
        }

        if (command.Origin.IpAddress is { } ip)
        {
            var perIp = await dbContext.Set<OtpChallenge>()
                .CountAsync(c => c.RequestedByIp == ip && c.CreatedAtUtc >= windowStart, cancellationToken);

            if (perIp >= _options.OtpPerIpPerWindow)
            {
                return Result.Failure(IdentityErrors.OtpRateLimited);
            }
        }

        var code = OtpCodes.Generate();

        dbContext.Set<OtpChallenge>().Add(OtpChallenge.Issue(
            OtpPurpose.Login,
            mobile,
            OtpCodes.Hash(mobile, code),
            now,
            now.AddMinutes(_options.OtpLifetimeMinutes),
            command.Origin.IpAddress));

        await dbContext.SaveChangesAsync(cancellationToken);

        await smsSender.SendAsync(
            new SmsMessage(
                mobile,
                $"{code} is your UP Bazaar verification code. It expires in "
                + $"{_options.OtpLifetimeMinutes} minutes. Do not share it with anyone."),
            cancellationToken);

        return Result.Success();
    }
}

/// <summary>Exchanges a mobile number and code for tokens, creating the buyer if new.</summary>
public sealed record VerifyOtpCommand(
    string Mobile,
    string Code,
    string? DisplayName,
    RequestOrigin Origin) : ICommand<AuthTokensDto>;

internal sealed class VerifyOtpCommandValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpCommandValidator()
    {
        RuleFor(x => x.Mobile).NotEmpty().Matches("^[6-9][0-9]{9}$")
            .WithMessage("Enter a valid 10-digit Indian mobile number.");
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$");
        RuleFor(x => x.DisplayName).MaximumLength(128);
    }
}

/// <summary>
/// Verifies a code and signs the buyer in, creating the account on first success.
///
/// Auto-creation is what makes mobile sign-in feel like no sign-up at all, and it is safe
/// here precisely because possession of the number has just been proved.
/// </summary>
internal sealed class VerifyOtpCommandHandler(
    UPBazaarDbContext dbContext,
    TokenService tokenService,
    LoginAuditWriter auditWriter,
    IClock clock) : ICommandHandler<VerifyOtpCommand, AuthTokensDto>
{
    public async Task<Result<AuthTokensDto>> HandleAsync(
        VerifyOtpCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var mobile = command.Mobile.Trim();

        var challenge = await dbContext.Set<OtpChallenge>()
            .Where(c => c.Target == mobile && c.Purpose == OtpPurpose.Login && c.ConsumedAtUtc == null)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (challenge is null)
        {
            auditWriter.RecordFailure(
                null, mobile, LoginMethod.Otp, LoginFailureReason.InvalidOtp, command.Origin);

            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthTokensDto>(IdentityErrors.OtpNotFound);
        }

        var verification = challenge.Verify(OtpCodes.Hash(mobile, command.Code), now);

        if (verification.IsFailure)
        {
            auditWriter.RecordFailure(
                null, mobile, LoginMethod.Otp, LoginFailureReason.InvalidOtp, command.Origin);

            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthTokensDto>(verification.Error);
        }

        var user = await dbContext.Set<User>()
            .Include(u => u.Roles)
            .ThenInclude(r => r.Role)
            .ThenInclude(r => r.Permissions)
            .ThenInclude(p => p.Permission)
            .FirstOrDefaultAsync(u => u.Mobile == mobile, cancellationToken);

        if (user is null)
        {
            user = await CreateBuyerAsync(mobile, command.DisplayName, now, cancellationToken);
        }
        else if (!user.CanSignIn)
        {
            auditWriter.RecordFailure(
                user.Id, mobile, LoginMethod.Otp, LoginFailureReason.Suspended, command.Origin);

            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthTokensDto>(IdentityErrors.AccountNotActive);
        }

        user.RegisterSuccessfulAccess(now);

        var tokens = await tokenService.IssueAsync(user, command.Origin, cancellationToken);

        auditWriter.RecordSuccess(user.Id, LoginMethod.Otp, command.Origin);

        await dbContext.SaveChangesAsync(cancellationToken);

        return tokens;
    }

    private async Task<User> CreateBuyerAsync(
        string mobile,
        string? displayName,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Last four digits, so a brand-new buyer has something recognisable on screen before
        // they have told us their name.
        var name = string.IsNullOrWhiteSpace(displayName)
            ? $"Buyer {mobile[^4..]}"
            : displayName.Trim();

        var user = User.CreateBuyerWithMobile(mobile, name, language: "en");

        var buyerRole = await dbContext.Set<Role>()
            .Include(r => r.Permissions)
            .ThenInclude(p => p.Permission)
            .FirstOrDefaultAsync(r => r.Name == PermissionCatalog.RoleNames.Buyer, cancellationToken);

        dbContext.Set<User>().Add(user);

        // Saved before the role is attached so the join row has a real user id.
        await dbContext.SaveChangesAsync(cancellationToken);

        if (buyerRole is not null)
        {
            user.AssignRole(buyerRole, now, assignedBy: "otp-signup");
        }

        return user;
    }
}

/// <summary>Generation and hashing of one-time codes.</summary>
public static class OtpCodes
{
    private const int Digits = 6;

    /// <summary>
    /// Six digits from a cryptographic source. Six is the usable limit for something typed off
    /// a lock screen; the attempt cap and five-minute expiry are what make it safe, not length.
    /// </summary>
    public static string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString(CultureInfo.InvariantCulture)
            .PadLeft(Digits, '0');

    /// <summary>
    /// Hashes a code, salted with the target.
    ///
    /// The salt matters: without it the six-digit space is small enough to precompute, and a
    /// leaked table would let anyone match hashes across every outstanding challenge at once.
    /// </summary>
    public static string Hash(string target, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{target}:{code}")))
            .ToLowerInvariant();
}
