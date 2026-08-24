using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Infrastructure.Identity;

/// <summary>Reads the authenticated principal off the ambient HTTP context.</summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    /// <summary>Claim type carrying a single granted permission.</summary>
    public const string PermissionClaimType = "permission";

    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    /// <inheritdoc />
    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal?.FindFirstValue("sub");

    /// <inheritdoc />
    public string? UserName => Principal?.FindFirstValue(ClaimTypes.Name)
        ?? Principal?.FindFirstValue("preferred_username");

    /// <inheritdoc />
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public bool HasPermission(string permission) =>
        Principal?.HasClaim(PermissionClaimType, permission) ?? false;
}

/// <summary>
/// Stands in for a principal when work runs outside a request, such as an outbox handler or a
/// Hangfire job. It is not authenticated, so audit rows attribute the change to the system
/// rather than to whoever happened to trigger it.
/// </summary>
public sealed class SystemUser : ICurrentUser
{
    /// <inheritdoc />
    public string? UserId => "system";

    /// <inheritdoc />
    public string? UserName => "system";

    /// <inheritdoc />
    public bool IsAuthenticated => false;

    /// <inheritdoc />
    public bool HasPermission(string permission) => true;
}
