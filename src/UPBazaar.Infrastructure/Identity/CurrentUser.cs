using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using UPBazaar.Infrastructure.Authorization;
using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Infrastructure.Identity;

/// <summary>Reads the authenticated principal off the ambient HTTP context.</summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal?.FindFirstValue("sub");

    public string? UserName => Principal?.FindFirstValue(ClaimTypes.Name)
        ?? Principal?.FindFirstValue("preferred_username");

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool HasPermission(string permission) =>
        Principal?.HasClaim(PermissionClaims.ClaimType, permission) ?? false;
}

/// <summary>Background work (outbox, Hangfire jobs) runs without an HTTP principal.</summary>
public sealed class SystemUser : ICurrentUser
{
    public string? UserId => "system";

    public string? UserName => "system";

    public bool IsAuthenticated => false;

    public bool HasPermission(string permission) => true;
}
