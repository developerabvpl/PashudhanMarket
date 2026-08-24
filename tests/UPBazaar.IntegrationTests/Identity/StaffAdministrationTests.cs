using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Infrastructure.Persistence.Shared;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Identity.Services;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Identity;

[Collection(ApiCollection.Name)]
public sealed class StaffAdministrationTests(ApiFixture fixture)
{
    private const string StaffPassword = "staff-initial-passphrase";

    private static readonly Uri AdminUsers = new("/api/v1/admin/users", UriKind.Relative);
    private static readonly Uri AdminRoles = new("/api/v1/admin/roles", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task The_seeded_super_admin_can_sign_in_and_holds_every_permission()
    {
        var tokens = await _auth.SignInAsSuperAdminAsync();

        var me = await fixture.CreateAuthenticatedClient(tokens.AccessToken)
            .GetFromJsonAsync<UserDto>(new Uri("/api/v1/users/me", UriKind.Relative));

        me!.Roles.ShouldContain(PermissionCatalog.RoleNames.SuperAdmin);
        me.Permissions.Count.ShouldBe(PermissionCatalog.All.Count);
        me.UserType.ShouldBe("Staff");
    }

    [DatabaseFact]
    public async Task Seeded_roles_all_exist()
    {
        var tokens = await _auth.SignInAsSuperAdminAsync();

        var roles = await fixture.CreateAuthenticatedClient(tokens.AccessToken)
            .GetFromJsonAsync<IReadOnlyList<RoleDto>>(AdminRoles);

        roles.ShouldNotBeNull();

        foreach (var expected in PermissionCatalog.Roles)
        {
            roles.ShouldContain(r => r.Name == expected.Name);
        }
    }

    [DatabaseFact]
    public async Task An_administrator_can_create_a_staff_user_and_it_is_audited()
    {
        var admin = await _auth.SignInAsSuperAdminAsync();
        var client = fixture.CreateAuthenticatedClient(admin.AccessToken);
        var email = AuthClient.NewEmail("staff");

        var response = await client.PostAsJsonAsync(AdminUsers, new
        {
            email,
            password = StaffPassword,
            displayName = "Ops Person",
            preferredLanguage = "en",
            roles = new[] { PermissionCatalog.RoleNames.SupportAgent },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<UserDto>();
        created!.UserType.ShouldBe("Staff");
        created.Roles.ShouldContain(PermissionCatalog.RoleNames.SupportAgent);

        // The audit row is written by the persistence interceptor, not by the handler.
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var audit = await dbContext.Set<AuditLog>()
            .AsNoTracking()
            .Where(a => a.EntityType == "User" && a.EntityPublicId == created.Id)
            .OrderBy(a => a.OccurredAtUtc)
            .ToListAsync();

        audit.ShouldNotBeEmpty();
        audit[0].Action.ShouldBe(AuditAction.Created);
        audit[0].Module.ShouldBe("identity");
        audit[0].CorrelationId.ShouldNotBeNullOrWhiteSpace();
    }

    [DatabaseFact]
    public async Task An_audited_change_never_records_the_password_hash()
    {
        var admin = await _auth.SignInAsSuperAdminAsync();
        var client = fixture.CreateAuthenticatedClient(admin.AccessToken);

        var created = await (await client.PostAsJsonAsync(AdminUsers, new
        {
            email = AuthClient.NewEmail("redact"),
            password = StaffPassword,
            displayName = "Redaction Check",
            roles = Array.Empty<string>(),
        })).Content.ReadFromJsonAsync<UserDto>();

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var audit = await dbContext.Set<AuditLog>()
            .AsNoTracking()
            .FirstAsync(a => a.EntityPublicId == created!.Id);

        audit.Changes.ShouldNotBeNull();
        audit.Changes.ShouldContain("PasswordHash");
        audit.Changes.ShouldContain("***redacted***");
        audit.Changes.ShouldNotContain(StaffPassword);
    }

    [DatabaseFact]
    public async Task Roles_can_be_replaced()
    {
        var admin = await _auth.SignInAsSuperAdminAsync();
        var client = fixture.CreateAuthenticatedClient(admin.AccessToken);

        var created = await (await client.PostAsJsonAsync(AdminUsers, new
        {
            email = AuthClient.NewEmail("roleswap"),
            password = StaffPassword,
            displayName = "Role Swap",
            roles = new[] { PermissionCatalog.RoleNames.SupportAgent },
        })).Content.ReadFromJsonAsync<UserDto>();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/admin/users/{created!.Id}/roles", UriKind.Relative),
            new { roles = new[] { PermissionCatalog.RoleNames.FinanceOfficer } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<UserDto>();
        updated!.Roles.ShouldBe([PermissionCatalog.RoleNames.FinanceOfficer]);
    }

    [DatabaseFact]
    public async Task An_unknown_role_is_rejected()
    {
        var admin = await _auth.SignInAsSuperAdminAsync();
        var client = fixture.CreateAuthenticatedClient(admin.AccessToken);

        var response = await client.PostAsJsonAsync(AdminUsers, new
        {
            email = AuthClient.NewEmail("badrole"),
            password = StaffPassword,
            displayName = "Bad Role",
            roles = new[] { "NotARealRole" },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task An_administrator_cannot_change_their_own_roles()
    {
        var admin = await _auth.SignInAsSuperAdminAsync();
        var client = fixture.CreateAuthenticatedClient(admin.AccessToken);

        var me = await client.GetFromJsonAsync<UserDto>(
            new Uri("/api/v1/users/me", UriKind.Relative));

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/admin/users/{me!.Id}/roles", UriKind.Relative),
            new { roles = new[] { PermissionCatalog.RoleNames.Buyer } });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task Deactivating_a_user_ends_their_sessions()
    {
        var admin = await _auth.SignInAsSuperAdminAsync();
        var adminClient = fixture.CreateAuthenticatedClient(admin.AccessToken);
        var email = AuthClient.NewEmail("deactivate");

        var created = await (await adminClient.PostAsJsonAsync(AdminUsers, new
        {
            email,
            password = StaffPassword,
            displayName = "Soon Gone",
            roles = Array.Empty<string>(),
        })).Content.ReadFromJsonAsync<UserDto>();

        var staffTokens = await (await _auth.LoginAsync(email, StaffPassword))
            .Content.ReadFromJsonAsync<AuthResultDto>();

        (await adminClient.DeleteAsync(
                new Uri($"/api/v1/admin/users/{created!.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The refresh token is dead, and a fresh sign-in is refused.
        (await _auth.RefreshAsync(staffTokens!.Tokens!.RefreshToken)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        (await _auth.LoginAsync(email, StaffPassword)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    [DatabaseFact]
    public async Task Users_can_be_listed_and_paged()
    {
        var admin = await _auth.SignInAsSuperAdminAsync();
        var client = fixture.CreateAuthenticatedClient(admin.AccessToken);

        var page = await client.GetFromJsonAsync<PagedList<UserSummaryDto>>(
            new Uri("/api/v1/admin/users?page=1&pageSize=5&userType=Staff", UriKind.Relative));

        page.ShouldNotBeNull();
        page.PageSize.ShouldBe(5);
        page.Items.ShouldAllBe(u => u.UserType == "Staff");
    }
}
