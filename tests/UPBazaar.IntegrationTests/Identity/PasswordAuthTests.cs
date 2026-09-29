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
public sealed class PasswordAuthTests(ApiFixture fixture)
{
    private const string GoodPassword = "correct-horse-battery-staple";

    private static readonly Uri Register = new("/api/v1/auth/register", UriKind.Relative);
    private static readonly Uri Me = new("/api/v1/users/me", UriKind.Relative);
    private static readonly Uri ChangePassword = new("/api/v1/auth/change-password", UriKind.Relative);
    private static readonly Uri ForgotPassword = new("/api/v1/auth/forgot-password", UriKind.Relative);
    private static readonly Uri ResetPassword = new("/api/v1/auth/reset-password", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task A_buyer_can_register_and_then_sign_in()
    {
        var email = AuthClient.NewEmail("buyer");

        var registered = await RegisterAsync(email);
        registered.StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await _auth.LoginAsync(email, GoodPassword);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        result!.RequiresTwoFactor.ShouldBeFalse();
        result.Tokens.ShouldNotBeNull();

        var me = await fixture.CreateAuthenticatedClient(result.Tokens.AccessToken)
            .GetFromJsonAsync<UserDto>(Me);

        me!.Email.ShouldBe(email);
        me.UserType.ShouldBe("Buyer");
    }

    [DatabaseFact]
    public async Task A_new_buyer_can_shop_with_the_token_registration_hands_back()
    {
        var registered = await RegisterAsync(AuthClient.NewEmail("fresh"));
        var tokens = await registered.Content.ReadFromJsonAsync<AuthTokensDto>();

        var cart = await fixture.CreateAuthenticatedClient(tokens!.AccessToken)
            .GetAsync(new Uri("/api/v1/cart", UriKind.Relative));

        cart.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [DatabaseFact]
    public async Task Registering_the_same_address_twice_is_a_conflict()
    {
        var email = AuthClient.NewEmail("dup");

        (await RegisterAsync(email)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RegisterAsync(email)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task A_short_password_is_rejected()
    {
        var response = await fixture.CreateClient().PostAsJsonAsync(
            Register,
            new
            {
                email = AuthClient.NewEmail(),
                password = "short",
                displayName = "Too Short",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [DatabaseFact]
    public async Task An_unknown_account_and_a_wrong_password_answer_identically()
    {
        var email = AuthClient.NewEmail("known");
        await RegisterAsync(email);

        var wrongPassword = await _auth.LoginAsync(email, "definitely-not-the-password");
        var unknownAccount = await _auth.LoginAsync(AuthClient.NewEmail("nobody"), GoodPassword);

        // Any difference here - status, code or wording - is an account-enumeration oracle.
        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownAccount.StatusCode.ShouldBe(unknownAccount.StatusCode);
        (await wrongPassword.Content.ReadAsStringAsync())
            .ShouldBe(await unknownAccount.Content.ReadAsStringAsync());
    }

    [DatabaseFact]
    public async Task Five_failures_lock_the_account_for_fifteen_minutes()
    {
        var email = AuthClient.NewEmail("lockme");
        await RegisterAsync(email);

        for (var i = 0; i < User.MaxFailedAccessAttempts; i++)
        {
            (await _auth.LoginAsync(email, "wrong-password-here"))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // The right password no longer helps.
        var afterLockout = await _auth.LoginAsync(email, GoodPassword);

        afterLockout.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await afterLockout.Content.ReadAsStringAsync()).ShouldContain("locked_out");

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var user = await dbContext.Set<User>().AsNoTracking().FirstAsync(u => u.Email == email);

        user.LockoutEndUtc.ShouldNotBeNull();
        user.LockoutEndUtc.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(10));
        user.LockoutEndUtc.Value.ShouldBeLessThan(DateTime.UtcNow.AddMinutes(20));
    }

    [DatabaseFact]
    public async Task Failed_attempts_are_recorded_in_the_login_audit()
    {
        var email = AuthClient.NewEmail("audited");
        await RegisterAsync(email);
        await _auth.LoginAsync(email, "wrong-password-here");

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var audit = await dbContext.Set<LoginAudit>()
            .AsNoTracking()
            .Where(a => a.AttemptedIdentifier == email && !a.Succeeded)
            .OrderByDescending(a => a.OccurredAtUtc)
            .FirstOrDefaultAsync();

        audit.ShouldNotBeNull();
        audit.FailureReason.ShouldBe(LoginFailureReason.WrongPassword);
    }

    [DatabaseFact]
    public async Task Changing_a_password_ends_existing_sessions()
    {
        var email = AuthClient.NewEmail("rotate");
        var tokens = await (await RegisterAsync(email)).Content.ReadFromJsonAsync<AuthTokensDto>();

        var response = await fixture.CreateAuthenticatedClient(tokens!.AccessToken).PostAsJsonAsync(
            ChangePassword,
            new { currentPassword = GoodPassword, newPassword = "a-brand-new-passphrase" });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The refresh token issued before the change is dead.
        (await _auth.RefreshAsync(tokens.RefreshToken)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        // And the new password works.
        (await _auth.LoginAsync(email, "a-brand-new-passphrase")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    [DatabaseFact]
    public async Task Changing_a_password_needs_the_current_one()
    {
        var email = AuthClient.NewEmail("guard");
        var tokens = await (await RegisterAsync(email)).Content.ReadFromJsonAsync<AuthTokensDto>();

        var response = await fixture.CreateAuthenticatedClient(tokens!.AccessToken).PostAsJsonAsync(
            ChangePassword,
            new { currentPassword = "not-the-current-one", newPassword = "another-long-passphrase" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task A_forgotten_password_can_be_reset_with_the_emailed_token()
    {
        var email = AuthClient.NewEmail("forgot");
        await RegisterAsync(email);

        (await fixture.CreateClient().PostAsJsonAsync(ForgotPassword, new { email }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var token = fixture.Email.LatestResetTokenFor(email);
        token.ShouldNotBeNullOrWhiteSpace();

        var reset = await fixture.CreateClient().PostAsJsonAsync(
            ResetPassword,
            new { email, token, newPassword = "recovered-passphrase-42" });

        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _auth.LoginAsync(email, "recovered-passphrase-42")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    [DatabaseFact]
    public async Task Forgot_password_reports_success_for_an_unknown_address()
    {
        // Anything else would confirm which addresses have accounts.
        var response = await fixture.CreateClient().PostAsJsonAsync(
            ForgotPassword,
            new { email = AuthClient.NewEmail("ghost") });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [DatabaseFact]
    public async Task A_reset_token_works_only_once()
    {
        var email = AuthClient.NewEmail("once");
        await RegisterAsync(email);
        await fixture.CreateClient().PostAsJsonAsync(ForgotPassword, new { email });
        var token = fixture.Email.LatestResetTokenFor(email);

        await fixture.CreateClient().PostAsJsonAsync(
            ResetPassword,
            new { email, token, newPassword = "first-reset-passphrase" });

        var second = await fixture.CreateClient().PostAsJsonAsync(
            ResetPassword,
            new { email, token, newPassword = "second-reset-passphrase" });

        second.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private Task<HttpResponseMessage> RegisterAsync(string email) =>
        fixture.CreateClient().PostAsJsonAsync(
            Register,
            new
            {
                email,
                password = GoodPassword,
                displayName = "Test Buyer",
                preferredLanguage = "en",
            });
}
