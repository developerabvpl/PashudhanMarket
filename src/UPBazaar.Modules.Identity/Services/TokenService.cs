using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using UPBazaar.Infrastructure.Identity;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Identity.Contracts.Events;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Identity.Services;

/// <summary>Where a token request came from, for auditing and revocation.</summary>
/// <param name="IpAddress">Caller's IP, if known.</param>
/// <param name="UserAgent">Caller's user agent, if sent.</param>
public readonly record struct RequestOrigin(string? IpAddress, string? UserAgent);

/// <summary>
/// Issues and rotates tokens.
///
/// Access tokens are self-contained JWTs carrying the user's permissions, so authorising a
/// request needs no database round trip. Refresh tokens are opaque random values stored only
/// as hashes, rotated on every use, and grouped into families so that presenting a rotated
/// token can be recognised as theft.
/// </summary>
public sealed class TokenService(
    UPBazaarDbContext dbContext,
    IOptions<IdentityModuleOptions> options,
    JwtSettings jwt,
    IClock clock)
{
    private const int RefreshTokenBytes = 32;

    private readonly IdentityModuleOptions _options = options.Value;

    /// <summary>Issues a fresh access and refresh token pair, starting a new token family.</summary>
    public async Task<AuthTokensDto> IssueAsync(
        User user,
        RequestOrigin origin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return await IssueInternalAsync(user, Guid.CreateVersion7(), origin, cancellationToken);
    }

    /// <summary>
    /// Exchanges a refresh token for the next pair.
    ///
    /// Presenting a token that was already rotated means either a replay or a stolen token.
    /// The module cannot tell which, so it assumes the worse case and revokes the entire
    /// family: the legitimate user is signed out of that session and has to sign in again,
    /// which is a far better outcome than an attacker keeping a live session.
    /// </summary>
    public async Task<Result<AuthTokensDto>> RotateAsync(
        string refreshToken,
        RequestOrigin origin,
        CancellationToken cancellationToken)
    {
        var hash = HashToken(refreshToken);
        var now = clock.UtcNow;

        var stored = await dbContext.Set<RefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            return Result.Failure<AuthTokensDto>(IdentityErrors.RefreshTokenInvalid);
        }

        var user = await dbContext.Set<User>()
            .Include(u => u.Roles)
            .ThenInclude(r => r.Role)
            .ThenInclude(r => r.Permissions)
            .ThenInclude(p => p.Permission)
            .FirstOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<AuthTokensDto>(IdentityErrors.RefreshTokenInvalid);
        }

        if (stored.IsRevoked)
        {
            await RevokeFamilyAsync(stored.FamilyId, now, origin.IpAddress, cancellationToken);

            user.RaiseReuseDetected(now, origin.IpAddress);

            return Result.Failure<AuthTokensDto>(IdentityErrors.RefreshTokenReuse);
        }

        if (stored.IsExpired(now))
        {
            return Result.Failure<AuthTokensDto>(IdentityErrors.RefreshTokenInvalid);
        }

        if (!user.CanSignIn)
        {
            return Result.Failure<AuthTokensDto>(IdentityErrors.AccountNotActive);
        }

        var issued = await IssueInternalAsync(user, stored.FamilyId, origin, cancellationToken);

        stored.Revoke(
            now,
            RefreshTokenRevocationReason.Rotated,
            origin.IpAddress,
            HashToken(issued.RefreshToken));

        return issued;
    }

    /// <summary>Revokes one token, used by sign-out.</summary>
    public async Task<Result> RevokeAsync(
        string refreshToken,
        RequestOrigin origin,
        CancellationToken cancellationToken)
    {
        var hash = HashToken(refreshToken);

        var stored = await dbContext.Set<RefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        // Signing out with a token that is already dead is not an error worth reporting.
        stored?.Revoke(clock.UtcNow, RefreshTokenRevocationReason.SignedOut, origin.IpAddress);

        return Result.Success();
    }

    /// <summary>Ends every session for a user, for a password change or a suspension.</summary>
    public async Task RevokeAllForUserAsync(
        long userId,
        RefreshTokenRevocationReason reason,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var active = await dbContext.Set<RefreshToken>()
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in active)
        {
            token.Revoke(now, reason);
        }
    }

    /// <summary>
    /// Short-lived token naming a half-finished sign-in, issued when a password was correct
    /// but a TOTP code is still outstanding. It grants nothing on its own.
    /// </summary>
    /// <summary>
    /// The audience a two-factor ticket is issued for: not the API's, so the API never accepts a
    /// ticket as an access token. Only the second step of signing in reads it.
    /// </summary>
    private string TwoFactorAudience => $"{Audience}:two-factor";

    public string IssueTwoFactorTicket(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = TwoFactorAudience,
            Expires = clock.UtcNow.AddMinutes(5),
            SigningCredentials = SigningCredentials,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [JwtRegisteredClaimNames.Sub] = user.PublicId.ToString(),
                ["amr"] = "pwd",
                ["purpose"] = "two_factor",
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Reads the subject out of a two-factor ticket, or null when it is not valid.</summary>
    public async Task<Guid?> ReadTwoFactorTicketAsync(string ticket)
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(ticket, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = Issuer,
            ValidAudience = TwoFactorAudience,
            IssuerSigningKey = SigningKey,
            ClockSkew = TimeSpan.FromSeconds(30),
        });

        if (!result.IsValid
            || !result.Claims.TryGetValue("purpose", out var purpose)
            || purpose as string != "two_factor"
            || !result.Claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var sub))
        {
            return null;
        }

        return Guid.TryParse(sub as string, out var userId) ? userId : null;
    }

    /// <summary>SHA-256, hex encoded. Refresh tokens are high-entropy, so no salt is needed.</summary>
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private async Task<AuthTokensDto> IssueInternalAsync(
        User user,
        Guid familyId,
        RequestOrigin origin,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var permissions = CollectPermissions(user);

        var accessToken = CreateAccessToken(user, permissions, now);

        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
        var refreshExpiry = now.AddDays(_options.RefreshTokenDays);

        dbContext.Set<RefreshToken>().Add(RefreshToken.Issue(
            user.Id,
            HashToken(refreshToken),
            familyId,
            now,
            refreshExpiry,
            origin.IpAddress,
            origin.UserAgent));

        await Task.CompletedTask;

        return new AuthTokensDto(
            accessToken,
            _options.AccessTokenMinutes * 60,
            refreshToken,
            refreshExpiry);
    }

    private string CreateAccessToken(User user, IReadOnlyCollection<string> permissions, DateTime now)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.PublicId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(ClaimTypes.NameIdentifier, user.PublicId.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new("user_type", user.UserType.ToString()),
        };

        if (user.Email is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
        }

        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r.Role.Name)));

        // Permissions travel in the token so authorisation is a claim check, not a query.
        claims.AddRange(permissions.Select(p => new Claim(CurrentUser.PermissionClaimType, p)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = now.AddMinutes(_options.AccessTokenMinutes),
            SigningCredentials = SigningCredentials,
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static IReadOnlyCollection<string> CollectPermissions(User user) =>
        [.. user.Roles
            .SelectMany(r => r.Role.Permissions)
            .Select(p => p.Permission.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)];

    private async Task RevokeFamilyAsync(
        Guid familyId,
        DateTime now,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var family = await dbContext.Set<RefreshToken>()
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in family)
        {
            token.Revoke(now, RefreshTokenRevocationReason.ReuseDetected, ipAddress);
        }
    }

    private string Issuer => jwt.Issuer;

    private string Audience => jwt.Audience;

    private SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(jwt.SigningKey));

    private SigningCredentials SigningCredentials =>
        new(SigningKey, SecurityAlgorithms.HmacSha256);
}
