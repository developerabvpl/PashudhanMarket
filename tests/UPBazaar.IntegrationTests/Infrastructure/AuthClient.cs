using System.Net.Http.Json;
using UPBazaar.Modules.Identity.Contracts.Dtos;

namespace UPBazaar.IntegrationTests.Infrastructure;

/// <summary>
/// Drives the auth endpoints over HTTP so tests read like the flows they exercise rather than
/// like a pile of request plumbing.
/// </summary>
public sealed class AuthClient(ApiFixture fixture)
{
    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);
    private static readonly Uri RequestOtp = new("/api/v1/auth/request-otp", UriKind.Relative);
    private static readonly Uri VerifyOtp = new("/api/v1/auth/verify-otp", UriKind.Relative);
    private static readonly Uri Refresh = new("/api/v1/auth/refresh", UriKind.Relative);

    /// <summary>Signs in the seeded administrator and returns its tokens.</summary>
    public async Task<AuthTokensDto> SignInAsSuperAdminAsync()
    {
        var response = await fixture.CreateClient().PostAsJsonAsync(
            Login,
            new { email = ApiFixture.SuperAdminEmail, password = ApiFixture.SuperAdminPassword });

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();

        return result?.Tokens
            ?? throw new InvalidOperationException("The seeded SuperAdmin could not sign in.");
    }

    /// <summary>Runs the whole buyer OTP flow and returns the tokens it produced.</summary>
    public async Task<AuthTokensDto> SignInBuyerByOtpAsync(string mobile, string? displayName = null)
    {
        (await fixture.CreateClient().PostAsJsonAsync(RequestOtp, new { mobile }))
            .EnsureSuccessStatusCode();

        var code = fixture.Sms.LatestCodeFor(mobile)
            ?? throw new InvalidOperationException($"No code was sent to {mobile}.");

        var response = await fixture.CreateClient().PostAsJsonAsync(
            VerifyOtp,
            new { mobile, code, displayName });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AuthTokensDto>()
            ?? throw new InvalidOperationException("Verification returned no tokens.");
    }

    /// <summary>Exchanges a refresh token, returning the raw response so a test can assert on it.</summary>
    public Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        fixture.CreateClient().PostAsJsonAsync(Refresh, new { refreshToken });

    /// <summary>Attempts a password sign-in, returning the raw response.</summary>
    public Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        fixture.CreateClient().PostAsJsonAsync(Login, new { email, password });

    /// <summary>A unique 10-digit Indian mobile number, so tests never collide.</summary>
    public static string NewMobile()
    {
        // Starts with 9 to satisfy the 6-9 rule; the rest is random.
        var suffix = Random.Shared.Next(100_000_000, 999_999_999);

        return $"9{suffix}";
    }

    /// <summary>A unique email address, so tests never collide.</summary>
    public static string NewEmail(string prefix = "user") =>
        $"{prefix}-{Guid.NewGuid():N}@upbazaar.test";
}
