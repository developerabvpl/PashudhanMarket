using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Identity.Domain;

namespace UPBazaar.IntegrationTests.Identity;

[Collection(ApiCollection.Name)]
public sealed class OtpAuthTests(ApiFixture fixture)
{
    private static readonly Uri RequestOtp = new("/api/v1/auth/request-otp", UriKind.Relative);
    private static readonly Uri VerifyOtp = new("/api/v1/auth/verify-otp", UriKind.Relative);
    private static readonly Uri Me = new("/api/v1/users/me", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task A_new_number_gets_a_code_an_account_and_tokens()
    {
        var mobile = AuthClient.NewMobile();

        var requested = await fixture.CreateClient().PostAsJsonAsync(RequestOtp, new { mobile });
        requested.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var code = fixture.Sms.LatestCodeFor(mobile);
        code.ShouldNotBeNull();
        code.Length.ShouldBe(6);

        var verified = await fixture.CreateClient().PostAsJsonAsync(
            VerifyOtp,
            new { mobile, code, displayName = "Asha Devi" });

        verified.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tokens = await verified.Content.ReadFromJsonAsync<AuthTokensDto>();
        tokens.ShouldNotBeNull();
        tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        tokens.ExpiresInSeconds.ShouldBe(15 * 60);

        // The account was created by the act of verifying, and the token works.
        var me = await fixture.CreateAuthenticatedClient(tokens.AccessToken)
            .GetFromJsonAsync<UserDto>(Me);

        me.ShouldNotBeNull();
        me.Mobile.ShouldBe(mobile);
        me.MobileVerified.ShouldBeTrue();
        me.UserType.ShouldBe("Buyer");
        me.DisplayName.ShouldBe("Asha Devi");
        me.Roles.ShouldContain("Buyer");
    }

    [DatabaseFact]
    public async Task Signing_in_again_reuses_the_same_account()
    {
        var mobile = AuthClient.NewMobile();

        var first = await _auth.SignInBuyerByOtpAsync(mobile);
        var second = await _auth.SignInBuyerByOtpAsync(mobile);

        var firstMe = await fixture.CreateAuthenticatedClient(first.AccessToken)
            .GetFromJsonAsync<UserDto>(Me);
        var secondMe = await fixture.CreateAuthenticatedClient(second.AccessToken)
            .GetFromJsonAsync<UserDto>(Me);

        secondMe!.Id.ShouldBe(firstMe!.Id);
    }

    [DatabaseFact]
    public async Task A_wrong_code_is_refused()
    {
        var mobile = AuthClient.NewMobile();
        await fixture.CreateClient().PostAsJsonAsync(RequestOtp, new { mobile });

        var response = await fixture.CreateClient().PostAsJsonAsync(
            VerifyOtp,
            new { mobile, code = "000000" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task A_code_cannot_be_used_twice()
    {
        var mobile = AuthClient.NewMobile();
        await fixture.CreateClient().PostAsJsonAsync(RequestOtp, new { mobile });
        var code = fixture.Sms.LatestCodeFor(mobile);

        (await fixture.CreateClient().PostAsJsonAsync(VerifyOtp, new { mobile, code }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var replay = await fixture.CreateClient().PostAsJsonAsync(VerifyOtp, new { mobile, code });

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task Five_wrong_guesses_burn_the_challenge()
    {
        var mobile = AuthClient.NewMobile();
        await fixture.CreateClient().PostAsJsonAsync(RequestOtp, new { mobile });
        var realCode = fixture.Sms.LatestCodeFor(mobile);

        for (var i = 0; i < OtpChallenge.MaxAttempts; i++)
        {
            await fixture.CreateClient().PostAsJsonAsync(VerifyOtp, new { mobile, code = "000001" });
        }

        // Even the correct code no longer works: the attempt budget is spent.
        var response = await fixture.CreateClient().PostAsJsonAsync(
            VerifyOtp,
            new { mobile, code = realCode });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [DatabaseFact]
    public async Task A_number_is_rate_limited_after_three_requests()
    {
        var mobile = AuthClient.NewMobile();

        for (var i = 0; i < 3; i++)
        {
            (await fixture.CreateClient().PostAsJsonAsync(RequestOtp, new { mobile }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var fourth = await fixture.CreateClient().PostAsJsonAsync(RequestOtp, new { mobile });

        fourth.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [DatabaseTheory]
    [InlineData("12345")]
    [InlineData("5876543210")]
    [InlineData("98765432101")]
    [InlineData("")]
    public async Task A_malformed_number_is_rejected(string mobile)
    {
        var response = await fixture.CreateClient().PostAsJsonAsync(RequestOtp, new { mobile });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [DatabaseFact]
    public async Task The_code_is_stored_only_as_a_hash()
    {
        var mobile = AuthClient.NewMobile();
        await fixture.CreateClient().PostAsJsonAsync(RequestOtp, new { mobile });
        var code = fixture.Sms.LatestCodeFor(mobile);

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var challenge = await dbContext.Set<OtpChallenge>()
            .AsNoTracking()
            .FirstAsync(c => c.Target == mobile);

        // Someone reading the table must not be able to complete the sign-in.
        challenge.CodeHash.ShouldNotBe(code);
        challenge.CodeHash.Length.ShouldBe(64);
    }

    [DatabaseFact]
    public async Task A_successful_sign_in_is_recorded_in_the_login_audit()
    {
        var mobile = AuthClient.NewMobile();
        await _auth.SignInBuyerByOtpAsync(mobile);

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var audit = await dbContext.Set<LoginAudit>()
            .AsNoTracking()
            .Where(a => a.Method == LoginMethod.Otp && a.Succeeded)
            .OrderByDescending(a => a.OccurredAtUtc)
            .FirstOrDefaultAsync();

        audit.ShouldNotBeNull();
        audit.CorrelationId.ShouldNotBeNullOrWhiteSpace();
    }
}
