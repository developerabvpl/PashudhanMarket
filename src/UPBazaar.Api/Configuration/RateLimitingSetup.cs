using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure.Api;

namespace UPBazaar.Api.Configuration;

/// <summary>How many sign-in requests one address may make, and over what window.</summary>
public sealed class SignInRateLimitOptions
{
    public const string SectionName = "RateLimits:SignIn";

    /// <summary>Requests allowed per window per address, across every sign-in endpoint.</summary>
    [Range(1, 1_000_000)]
    public int PermitLimit { get; set; } = 20;

    /// <summary>The window, in seconds.</summary>
    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Caps how fast one address can try passwords, one-time codes and reset tokens.
///
/// Each account already locks after a few wrong guesses, but that does nothing against one
/// address trying a single guess on many accounts, or asking for code after code. This is the
/// second line: a fixed window per client address across all the sign-in endpoints together,
/// answered with 429 and a Retry-After once used up.
///
/// The address is the connection's, which behind a reverse proxy would be the proxy's, with every
/// client sharing one window. <see cref="ForwardedHeadersSetup"/> puts the visitor's address back
/// when a trusted proxy passes it on; a proxy on another machine has to be named there.
/// </summary>
public static class RateLimitingSetup
{
    public static IServiceCollection AddSignInRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SignInRateLimitOptions>()
            .Bind(configuration.GetSection(SignInRateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy(RateLimitPolicies.SignIn, context =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<SignInRateLimitOptions>>().Value;
                var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // The limit is part of the key, so a changed limit takes effect at once instead of
                // when the windows counted under the old one run out.
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{address}|{options.PermitLimit}|{options.WindowSeconds}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = TimeSpan.FromSeconds(options.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

            limiter.OnRejected = async (rejected, cancellationToken) =>
            {
                var http = rejected.HttpContext;

                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many attempts from here. Wait a minute and try again.",
                        Type = "https://upbazaar.dev/errors/http.429",
                    },
                });
            };
        });

        return services;
    }
}
