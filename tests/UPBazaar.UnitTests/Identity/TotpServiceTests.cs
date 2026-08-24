using System.Text;
using UPBazaar.Modules.Identity.Services;

namespace UPBazaar.UnitTests.Identity;

/// <summary>
/// Checks the TOTP implementation against the test vectors published in RFC 6238 appendix B.
///
/// These vectors are the reason it was safe to implement the algorithm here rather than take a
/// dependency: they pin the behaviour to the same output every authenticator app produces.
/// </summary>
public sealed class TotpServiceTests
{
    /// <summary>The RFC's 20-byte SHA-1 seed: the ASCII string "12345678901234567890".</summary>
    private static readonly byte[] RfcSeed = Encoding.ASCII.GetBytes("12345678901234567890");

    private readonly TotpService _totp = new();

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Matches_the_RFC_6238_test_vectors(long unixSeconds, string expected)
    {
        var moment = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        TotpService.ComputeCode(RfcSeed, moment).ShouldBe(expected);
    }

    [Fact]
    public void A_generated_secret_round_trips_through_base32()
    {
        var secret = _totp.GenerateSecret();

        var decoded = TotpService.FromBase32(secret);

        decoded.Length.ShouldBe(20);
        TotpService.ToBase32(decoded).ShouldBe(secret);
    }

    [Fact]
    public void Two_generated_secrets_differ()
    {
        _totp.GenerateSecret().ShouldNotBe(_totp.GenerateSecret());
    }

    [Fact]
    public void A_current_code_verifies()
    {
        var now = new DateTime(2026, 3, 14, 10, 0, 0, DateTimeKind.Utc);
        var secret = TotpService.ToBase32(RfcSeed);
        var code = TotpService.ComputeCode(RfcSeed, new DateTimeOffset(now, TimeSpan.Zero));

        _totp.VerifyCode(secret, code, now).ShouldBeTrue();
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(30)]
    public void A_code_one_period_out_still_verifies(int driftSeconds)
    {
        // Clock drift and typing time both land here; rejecting these would make the feature
        // feel broken on a phone whose clock is a few seconds off.
        var now = new DateTime(2026, 3, 14, 10, 0, 0, DateTimeKind.Utc);
        var secret = TotpService.ToBase32(RfcSeed);
        var code = TotpService.ComputeCode(RfcSeed, new DateTimeOffset(now, TimeSpan.Zero));

        _totp.VerifyCode(secret, code, now.AddSeconds(driftSeconds)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(-120)]
    [InlineData(120)]
    public void A_code_further_out_is_rejected(int driftSeconds)
    {
        var now = new DateTime(2026, 3, 14, 10, 0, 0, DateTimeKind.Utc);
        var secret = TotpService.ToBase32(RfcSeed);
        var code = TotpService.ComputeCode(RfcSeed, new DateTimeOffset(now, TimeSpan.Zero));

        _totp.VerifyCode(secret, code, now.AddSeconds(driftSeconds)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    public void A_malformed_code_is_rejected_rather_than_throwing(string code)
    {
        var secret = TotpService.ToBase32(RfcSeed);

        _totp.VerifyCode(secret, code, DateTime.UtcNow).ShouldBeFalse();
    }

    [Fact]
    public void A_malformed_secret_is_rejected_rather_than_throwing()
    {
        _totp.VerifyCode("not-base32!", "123456", DateTime.UtcNow).ShouldBeFalse();
    }

    [Fact]
    public void Spaces_in_a_typed_code_are_tolerated()
    {
        var now = new DateTime(2026, 3, 14, 10, 0, 0, DateTimeKind.Utc);
        var secret = TotpService.ToBase32(RfcSeed);
        var code = TotpService.ComputeCode(RfcSeed, new DateTimeOffset(now, TimeSpan.Zero));
        var spaced = $"{code[..3]} {code[3..]}";

        _totp.VerifyCode(secret, spaced, now).ShouldBeTrue();
    }

    [Fact]
    public void The_authenticator_uri_carries_the_issuer_secret_and_algorithm()
    {
        var uri = TotpService.BuildAuthenticatorUri("UP Bazaar", "ops@upbazaar.dev", "ABCDEF");

        uri.ShouldStartWith("otpauth://totp/UP%20Bazaar:ops%40upbazaar.dev");
        uri.ShouldContain("secret=ABCDEF");
        uri.ShouldContain("issuer=UP%20Bazaar");
        uri.ShouldContain("digits=6");
        uri.ShouldContain("period=30");
    }
}
