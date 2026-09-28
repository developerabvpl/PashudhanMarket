using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Domain;

namespace UPBazaar.IntegrationTests.Sellers;

/// <summary>
/// A shop's team: the owner adds people by the email of the account they registered, as Manager
/// or Dispatch, and takes them off again. Neither sees earnings, the team or the shop's details.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SellerTeamTests(ApiFixture fixture)
{
    private const string Password = "correct-horse-battery-staple";

    private static readonly Uri TeamUri = new("/api/v1/sellers/me/team", UriKind.Relative);
    private static readonly Uri OrdersUri = new("/api/v1/seller/orders", UriKind.Relative);
    private static readonly Uri ProductsUri = new("/api/v1/seller/catalog/products", UriKind.Relative);
    private static readonly Uri BalanceUri = new("/api/v1/seller/settlements/balance", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task An_owner_adds_changes_and_removes_team_members_with_the_access_their_role_gives()
    {
        var admin = await AdminClientAsync();
        var (sellerId, owner) = await ShopAsync(admin);
        var (email, refresh) = await AccountAsync();

        var added = await owner.PostAsJsonAsync(TeamUri, new { email = email.ToUpperInvariant(), role = "Dispatch" });
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        var member = (await added.Content.ReadFromJsonAsync<SellerMemberDto>())!;
        member.Role.ShouldBe("Dispatch");
        member.Email.ShouldBe(email);

        // Dispatch: orders, not products, earnings, the team or where couriers collect from.
        var (dispatch, refresh2) = await RefreshedAsync(refresh);
        var access = await dispatch.GetFromJsonAsync<SellerAccessDto>(new Uri("/api/v1/sellers/me/access", UriKind.Relative));
        access!.SellerId.ShouldBe(sellerId);
        access.Role.ShouldBe("Dispatch");
        (await dispatch.GetAsync(OrdersUri)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await dispatch.GetAsync(ProductsUri)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await dispatch.GetAsync(BalanceUri)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await dispatch.GetAsync(TeamUri)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await dispatch.PutAsJsonAsync(new Uri("/api/v1/seller/shipping/pickup-location", UriKind.Relative), new { name = "Mine" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Manager: products too, still no earnings.
        (await owner.PutAsJsonAsync(new Uri($"/api/v1/sellers/me/team/{member.Id}", UriKind.Relative), new { role = "Manager" }))
            .EnsureSuccessStatusCode();
        var (manager, _) = await RefreshedAsync(refresh2);
        (await manager.GetAsync(ProductsUri)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.GetAsync(BalanceUri)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var team = await owner.GetFromJsonAsync<List<SellerMemberDto>>(TeamUri);
        team!.ShouldHaveSingleItem().Role.ShouldBe("Manager");

        // Taken off the team, the shop is gone at once, whatever the token still carries.
        (await owner.DeleteAsync(new Uri($"/api/v1/sellers/me/team/{member.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await manager.GetAsync(ProductsUri)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.GetFromJsonAsync<List<SellerMemberDto>>(TeamUri))!.ShouldBeEmpty();
    }

    [DatabaseFact]
    public async Task Only_a_registered_account_not_already_in_a_shop_can_be_added()
    {
        var admin = await AdminClientAsync();
        var (_, owner) = await ShopAsync(admin);
        var (_, other) = await ShopAsync(admin);
        var (email, _) = await AccountAsync();

        (await owner.PostAsJsonAsync(TeamUri, new { email = $"nobody-{Guid.NewGuid():N}@example.com", role = "Manager" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.PostAsJsonAsync(TeamUri, new { email, role = "Owner" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await owner.PostAsJsonAsync(TeamUri, new { email, role = "Manager" })).EnsureSuccessStatusCode();
        (await other.PostAsJsonAsync(TeamUri, new { email, role = "Manager" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>A registered email account, as someone signing up on the seller portal makes.</summary>
    private async Task<(string Email, string RefreshToken)> AccountAsync()
    {
        var email = $"team-{Guid.NewGuid():N}@example.com";
        var registered = await fixture.CreateClient().PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, password = Password, displayName = "Ravi Packer", preferredLanguage = "en" });
        registered.StatusCode.ShouldBe(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());

        return (email, (await registered.Content.ReadFromJsonAsync<AuthTokensDto>())!.RefreshToken);
    }

    /// <summary>A client with a token issued now, carrying roles granted since the last one.</summary>
    private async Task<(HttpClient Client, string RefreshToken)> RefreshedAsync(string refreshToken)
    {
        var tokens = (await (await _auth.RefreshAsync(refreshToken)).Content.ReadFromJsonAsync<AuthTokensDto>())!;

        return (fixture.CreateAuthenticatedClient(tokens.AccessToken), tokens.RefreshToken);
    }

    /// <summary>An approved shop with its owner signed in.</summary>
    private async Task<(Guid Id, HttpClient Owner)> ShopAsync(HttpClient admin)
    {
        var id = Guid.NewGuid();

        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

            dbContext.Add(Seller.Seed(
                id,
                new SellerApplication(
                    "Team Gaushala", null, "9000000000", null, "5 Dairy Lane", null, "Lucknow", "Uttar Pradesh",
                    "226001", "Team Gaushala", null, "AAAAA0000A", "Team Gaushala", "112233445566", "SBIN0001234"),
                DateTime.UtcNow));

            await dbContext.SaveChangesAsync();
        }

        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile(), "Shop Owner");
        var me = await fixture.CreateAuthenticatedClient(tokens.AccessToken).GetFromJsonAsync<UserDto>(new Uri("/api/v1/users/me", UriKind.Relative));

        (await admin.PostAsJsonAsync(new Uri($"/api/v1/admin/sellers/{id}/owner", UriKind.Relative), new { ownerUserId = me!.Id }))
            .EnsureSuccessStatusCode();

        return (id, (await RefreshedAsync(tokens.RefreshToken)).Client);
    }

    private async Task<HttpClient> AdminClientAsync() =>
        fixture.CreateAuthenticatedClient((await _auth.SignInAsSuperAdminAsync()).AccessToken);
}
