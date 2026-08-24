using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Identity.Domain;
using UPBazaar.Modules.Identity.Services;

namespace UPBazaar.IntegrationTests.Identity;

[Collection(ApiCollection.Name)]
public sealed class RefreshTokenTests(ApiFixture fixture)
{
    private static readonly Uri Logout = new("/api/v1/auth/logout", UriKind.Relative);
    private static readonly Uri Me = new("/api/v1/users/me", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task A_refresh_returns_a_new_pair_and_retires_the_old_token()
    {
        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile());

        var response = await _auth.RefreshAsync(tokens.RefreshToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rotated = await response.Content.ReadFromJsonAsync<AuthTokensDto>();
        rotated.ShouldNotBeNull();
        rotated.RefreshToken.ShouldNotBe(tokens.RefreshToken);
        rotated.AccessToken.ShouldNotBeNullOrWhiteSpace();

        // The new access token works.
        (await fixture.CreateAuthenticatedClient(rotated.AccessToken).GetAsync(Me))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [DatabaseFact]
    public async Task Rotation_can_be_repeated()
    {
        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile());

        for (var i = 0; i < 3; i++)
        {
            var response = await _auth.RefreshAsync(tokens.RefreshToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            tokens = (await response.Content.ReadFromJsonAsync<AuthTokensDto>())!;
        }

        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();
    }

    [DatabaseFact]
    public async Task Presenting_a_rotated_token_again_is_refused_as_reuse()
    {
        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile());

        var rotated = await (await _auth.RefreshAsync(tokens.RefreshToken))
            .Content.ReadFromJsonAsync<AuthTokensDto>();

        // The original token has already been exchanged. Presenting it again is either a
        // replay or a stolen token.
        var reuse = await _auth.RefreshAsync(tokens.RefreshToken);

        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await reuse.Content.ReadAsStringAsync()).ShouldContain("reuse_detected");

        // And the successor is revoked too: the whole family goes, because the module cannot
        // tell the thief from the victim.
        var successorAfterReuse = await _auth.RefreshAsync(rotated!.RefreshToken);

        successorAfterReuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task Reuse_revokes_the_family_with_a_recorded_reason()
    {
        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile());
        await _auth.RefreshAsync(tokens.RefreshToken);
        await _auth.RefreshAsync(tokens.RefreshToken);

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var hash = TokenService.HashToken(tokens.RefreshToken);

        var family = await dbContext.Set<RefreshToken>()
            .AsNoTracking()
            .Where(t => t.FamilyId == dbContext.Set<RefreshToken>()
                .Where(x => x.TokenHash == hash)
                .Select(x => x.FamilyId)
                .First())
            .ToListAsync();

        family.Count.ShouldBeGreaterThanOrEqualTo(2);
        family.ShouldAllBe(t => t.RevokedAtUtc != null);
        family.ShouldContain(t => t.RevocationReason == RefreshTokenRevocationReason.ReuseDetected);
    }

    [DatabaseFact]
    public async Task Reuse_is_recorded_in_the_login_audit()
    {
        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile());
        await _auth.RefreshAsync(tokens.RefreshToken);
        await _auth.RefreshAsync(tokens.RefreshToken);

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var audit = await dbContext.Set<LoginAudit>()
            .AsNoTracking()
            .Where(a => a.FailureReason == LoginFailureReason.RefreshTokenReuse)
            .OrderByDescending(a => a.OccurredAtUtc)
            .FirstOrDefaultAsync();

        audit.ShouldNotBeNull();
    }

    [DatabaseFact]
    public async Task An_unknown_refresh_token_is_refused()
    {
        var response = await _auth.RefreshAsync("not-a-real-token");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task Signing_out_kills_the_refresh_token()
    {
        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile());

        var logout = await fixture.CreateClient().PostAsJsonAsync(
            Logout,
            new { refreshToken = tokens.RefreshToken });

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _auth.RefreshAsync(tokens.RefreshToken)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task Only_the_hash_of_a_refresh_token_is_stored()
    {
        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile());

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var storedRaw = await dbContext.Set<RefreshToken>()
            .AsNoTracking()
            .AnyAsync(t => t.TokenHash == tokens.RefreshToken);

        storedRaw.ShouldBeFalse();

        var storedHash = await dbContext.Set<RefreshToken>()
            .AsNoTracking()
            .AnyAsync(t => t.TokenHash == TokenService.HashToken(tokens.RefreshToken));

        storedHash.ShouldBeTrue();
    }
}
