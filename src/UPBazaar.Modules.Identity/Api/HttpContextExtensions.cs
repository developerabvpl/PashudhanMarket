using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using UPBazaar.Modules.Identity.Services;

namespace UPBazaar.Modules.Identity.Api;

/// <summary>Pulls the few request facts the identity handlers need off the HTTP context.</summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Caller IP and user agent, for auditing and rate limiting.
    ///
    /// The IP is taken from the connection, not from X-Forwarded-For: a header the client
    /// controls is worthless for rate limiting. When a proxy sits in front, configure
    /// ForwardedHeaders so the connection address is already the real one.
    /// </summary>
    public static RequestOrigin Origin(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new RequestOrigin(
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null);
    }

    /// <summary>The signed-in user's public id, or null when the request is anonymous.</summary>
    public static Guid? CurrentUserId(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
