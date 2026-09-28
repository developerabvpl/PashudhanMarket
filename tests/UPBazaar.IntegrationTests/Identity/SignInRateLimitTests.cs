using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UPBazaar.Api.Configuration;
using UPBazaar.IntegrationTests.Infrastructure;

namespace UPBazaar.IntegrationTests.Identity;

/// <summary>
/// One address can only try so many sign-ins in a window, whatever accounts it aims at, and is
/// told when to come back. Other endpoints are not counted.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SignInRateLimitTests(ApiFixture fixture)
{
    [DatabaseFact]
    public async Task Too_many_sign_in_attempts_from_one_address_are_refused_until_the_window_passes()
    {
        var options = fixture.Services.GetRequiredService<IOptions<SignInRateLimitOptions>>().Value;
        var (limit, window) = (options.PermitLimit, options.WindowSeconds);
        (options.PermitLimit, options.WindowSeconds) = (3, 60);

        try
        {
            var client = fixture.CreateClient();

            // Three guesses, each at a different account, all counted together.
            for (var i = 0; i < 3; i++)
            {
                (await LoginAsync(client, $"nobody-{Guid.NewGuid():N}@example.com")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            var refused = await LoginAsync(client, $"nobody-{Guid.NewGuid():N}@example.com");
            refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
            refused.Headers.RetryAfter.ShouldNotBeNull();

            // Across every sign-in endpoint, not per endpoint.
            (await client.PostAsJsonAsync(new Uri("/api/v1/auth/request-otp", UriKind.Relative), new { mobile = AuthClient.NewMobile() }))
                .StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

            // Everything else carries on.
            (await client.GetAsync(new Uri("/api/v1/catalog/categories", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            (options.PermitLimit, options.WindowSeconds) = (limit, window);
        }
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = "not-the-password-at-all" });
}
