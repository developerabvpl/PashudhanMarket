using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UPBazaar.Infrastructure.Identity;
using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Modules.Reviews.Photos;

/// <summary>
/// Addresses for review photos.
///
/// Photos on an approved review are public and get a plain address a browser can cache. The
/// rest - a buyer's photos still waiting, the ones staff are judging - must stay private, but
/// they are shown with image tags, which cannot send a bearer token. So their address carries
/// an expiry and an HMAC over photo and expiry: whoever the API gave the link to can load the
/// photo for the next hour, and nobody can make a link for a photo they were not given.
///
/// The HMAC key is derived from the JWT signing key rather than configured separately, so there
/// is no second secret to provision, and every server that can check a token can check a link.
/// </summary>
public sealed class PhotoLinks
{
    /// <summary>How long a private link works: long enough to moderate a page of reviews.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private const string BasePath = "/api/v1/reviews/photos/";

    /// <summary>31 Dec 9999, the last second <see cref="DateTimeOffset"/> can hold.</summary>
    private const long MaxUnixSeconds = 253_402_300_799;

    private readonly byte[] _key;
    private readonly IClock _clock;

    public PhotoLinks(JwtSettings jwt, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(jwt);

        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(jwt.SigningKey),
            outputLength: 32,
            info: "upbazaar.reviews.photo-links"u8.ToArray());
        _clock = clock;
    }

    /// <summary>The cacheable address of a photo on an approved review.</summary>
    public static string Public(Guid photoId) => BasePath + photoId.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>A signed address that works for <see cref="Lifetime"/>.</summary>
    public string Private(Guid photoId)
    {
        // Rounded up to the next ten minutes, so the same photo gets the same link on every
        // load within that span and the browser's cache can do its job.
        var expires = RoundUp(_clock.UtcNow + Lifetime);

        return $"{Public(photoId)}?expires={expires}&signature={Sign(photoId, expires)}";
    }

    /// <summary>True when the link was made here, for this photo, and has not yet expired.</summary>
    public bool IsValid(Guid photoId, long? expires, string? signature)
    {
        if (expires is not { } at
            || string.IsNullOrEmpty(signature)
            || at < 0
            || at > MaxUnixSeconds
            || DateTimeOffset.FromUnixTimeSeconds(at).UtcDateTime < _clock.UtcNow)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Sign(photoId, at)),
            Encoding.ASCII.GetBytes(signature));
    }

    private string Sign(Guid photoId, long expires) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{photoId:N}:{expires}")))
            .ToLowerInvariant();

    private static long RoundUp(DateTime utc)
    {
        const long step = 600;
        var seconds = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

        return (seconds + step - 1) / step * step;
    }
}
