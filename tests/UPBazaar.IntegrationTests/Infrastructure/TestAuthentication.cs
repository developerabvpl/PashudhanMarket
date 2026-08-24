using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure.Authorization;

namespace UPBazaar.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for the identity provider. The caller declares its permissions in a header, so
/// tests exercise the real policy provider and can assert that a missing permission is a 403.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    public const string PermissionsHeader = "X-Test-Permissions";

    public const string UserIdHeader = "X-Test-User-Id";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(PermissionsHeader, out var permissions))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = Request.Headers.TryGetValue(UserIdHeader, out var id)
            ? id.ToString()
            : "test-user";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId),
        };

        claims.AddRange(permissions
            .ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(permission => new Claim(PermissionClaims.ClaimType, permission)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }
}
