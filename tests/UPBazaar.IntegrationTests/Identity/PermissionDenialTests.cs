using System.Net;
using System.Net.Http.Json;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Identity.Services;

namespace UPBazaar.IntegrationTests.Identity;

/// <summary>
/// The authorization boundary: who is refused, and with which status.
///
/// 401 and 403 mean different things and both matter. Anonymous gets 401 ("who are you"),
/// while a signed-in user without the permission gets 403 ("you, but no") - collapsing the two
/// would send a legitimate user back to a login form they have already completed.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PermissionDenialTests(ApiFixture fixture)
{
    private const string StaffPassword = "denial-test-passphrase";

    private static readonly Uri AdminUsers = new("/api/v1/admin/users", UriKind.Relative);
    private static readonly Uri AdminRoles = new("/api/v1/admin/roles", UriKind.Relative);
    private static readonly Uri Me = new("/api/v1/users/me", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task An_anonymous_caller_gets_401_from_an_admin_endpoint()
    {
        (await fixture.CreateClient().GetAsync(AdminUsers)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task An_anonymous_caller_gets_401_from_the_profile_endpoint()
    {
        (await fixture.CreateClient().GetAsync(Me)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task A_buyer_gets_403_from_an_admin_endpoint()
    {
        var buyer = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile());

        var response = await fixture.CreateAuthenticatedClient(buyer.AccessToken).GetAsync(AdminUsers);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [DatabaseFact]
    public async Task A_buyer_can_still_read_their_own_profile()
    {
        var buyer = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile());

        (await fixture.CreateAuthenticatedClient(buyer.AccessToken).GetAsync(Me)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    [DatabaseFact]
    public async Task A_support_agent_may_read_users_but_not_create_them()
    {
        var agent = await CreateStaffAndSignInAsync(PermissionCatalog.RoleNames.SupportAgent);
        var client = fixture.CreateAuthenticatedClient(agent.AccessToken);

        // The role carries identity.users.read but not identity.users.manage.
        (await client.GetAsync(AdminUsers)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var created = await client.PostAsJsonAsync(AdminUsers, new
        {
            email = AuthClient.NewEmail("nope"),
            password = StaffPassword,
            displayName = "Should Not Exist",
            roles = Array.Empty<string>(),
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [DatabaseFact]
    public async Task A_support_supervisor_may_create_users()
    {
        var supervisor = await CreateStaffAndSignInAsync(PermissionCatalog.RoleNames.SupportSupervisor);

        var created = await fixture.CreateAuthenticatedClient(supervisor.AccessToken)
            .PostAsJsonAsync(AdminUsers, new
            {
                email = AuthClient.NewEmail("bysupervisor"),
                password = StaffPassword,
                displayName = "Created By Supervisor",
                roles = Array.Empty<string>(),
            });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [DatabaseFact]
    public async Task A_role_without_roles_read_is_refused_the_role_catalogue()
    {
        var agent = await CreateStaffAndSignInAsync(PermissionCatalog.RoleNames.CatalogModerator);

        var response = await fixture.CreateAuthenticatedClient(agent.AccessToken).GetAsync(AdminRoles);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [DatabaseFact]
    public async Task A_garbled_token_is_refused()
    {
        var response = await fixture.CreateAuthenticatedClient("not.a.jwt").GetAsync(Me);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>Creates a staff account holding one role and signs it in.</summary>
    private async Task<AuthTokensDto> CreateStaffAndSignInAsync(string role)
    {
        var admin = await _auth.SignInAsSuperAdminAsync();
        var email = AuthClient.NewEmail(role.ToLowerInvariant());

        var created = await fixture.CreateAuthenticatedClient(admin.AccessToken)
            .PostAsJsonAsync(AdminUsers, new
            {
                email,
                password = StaffPassword,
                displayName = $"{role} Person",
                roles = new[] { role },
            });

        created.EnsureSuccessStatusCode();

        var result = await (await _auth.LoginAsync(email, StaffPassword))
            .Content.ReadFromJsonAsync<AuthResultDto>();

        return result?.Tokens
            ?? throw new InvalidOperationException($"The {role} account could not sign in.");
    }
}
