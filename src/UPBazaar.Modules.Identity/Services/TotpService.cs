using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace UPBazaar.Modules.Identity.Services;

/// <summary>
/// RFC 6238 time-based one-time passwords, as produced by Google Authenticator and Authy.
///
/// Implemented here rather than taken as a dependency: the algorithm is HMAC-SHA1 over a
/// counter, it is short, and it is pinned by the published RFC test vectors that the unit
/// tests assert against. A third-party package for forty lines of well-specified arithmetic
/// would be more supply chain than it is worth.
/// </summary>
public sealed class TotpService
{
    private const int Digits = 6;
    private const int PeriodSeconds = 30;

    /// <summary>
    /// How many periods either side of "now" are accepted. One step covers ordinary clock
    /// drift and the seconds a user spends typing; more would widen the window an attacker has
    /// to replay an observed code.
    /// </summary>
    private const int AllowedDriftPeriods = 1;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Generates a 160-bit secret, the size RFC 4226 recommends for HMAC-SHA1.</summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Instance members keep the service substitutable through DI.")]
    public string GenerateSecret() => ToBase32(RandomNumberGenerator.GetBytes(20));

    /// <summary>
    /// Builds the otpauth URI an authenticator app scans.
    /// </summary>
    /// <param name="issuer">Shown as the account provider, e.g. "UP Bazaar".</param>
    /// <param name="accountName">Usually the user's email.</param>
    /// <param name="base32Secret">Secret from <see cref="GenerateSecret"/>.</param>
    /// <returns>An otpauth:// URI.</returns>
    public static string BuildAuthenticatorUri(string issuer, string accountName, string base32Secret)
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedAccount = Uri.EscapeDataString(accountName);

        return $"otpauth://totp/{encodedIssuer}:{encodedAccount}"
            + $"?secret={base32Secret}&issuer={encodedIssuer}&algorithm=SHA1"
            + $"&digits={Digits}&period={PeriodSeconds}";
    }

    /// <summary>Computes the code for one moment. Exposed for tests and for the RFC vectors.</summary>
    public static string ComputeCode(byte[] secret, DateTimeOffset moment)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var counter = moment.ToUnixTimeSeconds() / PeriodSeconds;

        return ComputeCodeForCounter(secret, counter);
    }

    /// <summary>
    /// Validates a presented code against the current period and one period either side.
    /// </summary>
    /// <param name="base32Secret">The user's stored secret.</param>
    /// <param name="code">Code the user typed.</param>
    /// <param name="utcNow">Current time.</param>
    /// <returns>True when the code matches an accepted period.</returns>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Instance members keep the service substitutable through DI.")]
    public bool VerifyCode(string base32Secret, string code, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var trimmed = code.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

        if (trimmed.Length != Digits)
        {
            return false;
        }

        byte[] secret;

        try
        {
            secret = FromBase32(base32Secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var counter = new DateTimeOffset(utcNow, TimeSpan.Zero).ToUnixTimeSeconds() / PeriodSeconds;

        for (var drift = -AllowedDriftPeriods; drift <= AllowedDriftPeriods; drift++)
        {
            var candidate = ComputeCodeForCounter(secret, counter + drift);

            // Fixed-time comparison: a timing oracle on a six-digit code is worth closing.
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(candidate),
                    Encoding.ASCII.GetBytes(trimmed)))
            {
                return true;
            }
        }

        return false;
    }

    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "RFC 6238 specifies HMAC-SHA1 for TOTP. Authenticator apps implement "
            + "that and nothing else, so a stronger hash here would simply not interoperate. "
            + "The construction is a MAC over a counter, not a collision-sensitive digest.")]
    private static string ComputeCodeForCounter(byte[] secret, long counter)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counterBytes, hash);

        // Dynamic truncation, RFC 4226 section 5.3.
        var offset = hash[^1] & 0x0F;

        var binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        var otp = binary % (int)Math.Pow(10, Digits);

        return otp.ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0');
    }

    /// <summary>Base32 without padding, the encoding authenticator apps expect.</summary>
    public static string ToBase32(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder((data.Length * 8 / 5) + 1);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                builder.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            builder.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }

        return builder.ToString();
    }

    /// <summary>Decodes Base32, tolerating padding and lower case.</summary>
    public static byte[] FromBase32(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        var cleaned = encoded.TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(cleaned.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var c in cleaned)
        {
            var index = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);

            if (index < 0)
            {
                throw new FormatException($"'{c}' is not a Base32 character.");
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        return [.. output];
    }
}
