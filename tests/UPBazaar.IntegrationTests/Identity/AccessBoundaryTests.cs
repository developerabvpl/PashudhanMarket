using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.Modules.Identity.Services;

namespace UPBazaar.IntegrationTests.Identity;

/// <summary>
/// Where access stops: a half-finished sign-in is not a session, and managing staff does not
/// reach past one's own permissions.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccessBoundaryTests(ApiFixture fixture)
{
    private const string StaffPassword = "correct-horse-battery-staple";

    private static readonly Uri AdminUsers = new("/api/v1/admin/users", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task A_two_factor_ticket_is_not_an_access_token()
    {
        string ticket;

        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();
            var admin = await dbContext.Set<User>().FirstAsync(u => u.Email == User.Normalize(ApiFixture.SuperAdminEmail));
            var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();

            ticket = tokens.IssueTwoFactorTicket(admin);
            (await tokens.ReadTwoFactorTicketAsync(ticket)).ShouldBe(admin.PublicId);
        }

        var client = fixture.CreateAuthenticatedClient(ticket);

        (await client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsync(new Uri("/api/v1/users/me/2fa/setup", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task Staff_cannot_grant_or_take_away_more_than_they_hold()
    {
        var superAdmin = fixture.CreateAuthenticatedClient((await _auth.SignInAsSuperAdminAsync()).AccessToken);
        var supervisor = await StaffAsync(superAdmin, PermissionCatalog.RoleNames.SupportSupervisor);

        // Within reach: an account with no role at all.
        (await supervisor.PostAsJsonAsync(AdminUsers, new { email = AuthClient.NewEmail("boundary"), password = StaffPassword, displayName = "New Hire", roles = Array.Empty<string>() }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Beyond it - an agent reads reviews and sellers, which a supervisor does not.
        (await CreateAsync(supervisor, PermissionCatalog.RoleNames.SupportAgent)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CreateAsync(supervisor, PermissionCatalog.RoleNames.SuperAdmin)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CreateAsync(supervisor, PermissionCatalog.RoleNames.Admin)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Nor can they strip or suspend someone who holds more than they do.
        var admin = await (await CreateAsync(superAdmin, PermissionCatalog.RoleNames.Admin)).Content.ReadFromJsonAsync<Modules.Identity.Contracts.Dtos.UserDto>();
        (await supervisor.PutAsJsonAsync(new Uri($"/api/v1/admin/users/{admin!.Id}/roles", UriKind.Relative), new { roles = Array.Empty<string>() }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await supervisor.DeleteAsync(new Uri($"/api/v1/admin/users/{admin.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient caller, string role) =>
        caller.PostAsJsonAsync(AdminUsers, new
        {
            email = AuthClient.NewEmail("boundary"),
            password = StaffPassword,
            displayName = "Boundary Test",
            preferredLanguage = "en",
            roles = new[] { role },
        });

    private async Task<HttpClient> StaffAsync(HttpClient superAdmin, string role)
    {
        var email = AuthClient.NewEmail("supervisor");

        (await superAdmin.PostAsJsonAsync(AdminUsers, new { email, password = StaffPassword, displayName = "Supervisor", roles = new[] { role } }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var signedIn = await (await _auth.LoginAsync(email, StaffPassword)).Content.ReadFromJsonAsync<Modules.Identity.Contracts.Dtos.AuthResultDto>();

        return fixture.CreateAuthenticatedClient(signedIn!.Tokens!.AccessToken);
    }
}
