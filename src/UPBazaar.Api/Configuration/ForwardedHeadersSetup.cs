using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace UPBazaar.Api.Configuration;

/// <summary>
/// Takes the caller's address and scheme from the reverse proxy in front of the API.
///
/// The site is served from one origin: IIS (or nginx) hands out the storefront and the portals
/// and forwards /api here. Every request therefore arrives from the proxy's address, and without
/// this the per-address cap on sign-in attempts would treat all visitors as one - the first
/// twenty sign-ins in a minute would lock everybody else out.
///
/// Only a known proxy is believed. X-Forwarded-For is an ordinary header anyone can send, so
/// honouring it from any caller would let a guesser pick a new "address" for every attempt. A
/// proxy on this machine (loopback) is trusted as it stands, which covers IIS with ARR and nginx
/// beside the API. A proxy on another machine must be named in <c>ForwardedHeaders:KnownProxies</c>.
/// </summary>
public static class ForwardedHeadersSetup
{
    public const string KnownProxiesKey = "ForwardedHeaders:KnownProxies";

    public static IServiceCollection AddProxyForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var proxies = configuration.GetSection(KnownProxiesKey).Get<string[]>() ?? [];

        return services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // One hop: the address the trusted proxy saw. Anything further left in the header was
            // written by whoever called the proxy, and is not evidence of anything.
            options.ForwardLimit = 1;

            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        });
    }
}
