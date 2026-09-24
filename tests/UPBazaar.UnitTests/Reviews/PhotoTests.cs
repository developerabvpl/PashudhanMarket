using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UPBazaar.Infrastructure.Identity;
using UPBazaar.Modules.Reviews.Photos;
using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.UnitTests.Reviews;

public sealed class PhotoTests
{
    private static readonly Guid PhotoId = Guid.Parse("01a0d000-0000-7000-8000-000000000001");

    [Fact]
    public void Photo_types_are_read_from_the_bytes()
    {
        PhotoFormat.Detect([0xFF, 0xD8, 0xFF, 0xE0]).ShouldBe("image/jpeg");
        PhotoFormat.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]).ShouldBe("image/png");
        PhotoFormat.Detect("RIFF\0\0\0\0WEBP"u8).ShouldBe("image/webp");

        PhotoFormat.Detect("GIF89a"u8).ShouldBeNull();
        PhotoFormat.Detect("<svg xmlns"u8).ShouldBeNull();
        PhotoFormat.Detect([]).ShouldBeNull();
    }

    [Fact]
    public void A_private_link_works_until_it_expires()
    {
        var clock = new StubClock { UtcNow = new DateTime(2026, 9, 24, 10, 3, 0, DateTimeKind.Utc) };
        var links = Links(clock);

        var (expires, signature) = Parse(links.Private(PhotoId));

        // Rounded up to ten minutes past the hour's lifetime: 11:10.
        DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime.ShouldBe(new DateTime(2026, 9, 24, 11, 10, 0, DateTimeKind.Utc));
        links.IsValid(PhotoId, expires, signature).ShouldBeTrue();

        clock.UtcNow = clock.UtcNow.AddHours(2);
        links.IsValid(PhotoId, expires, signature).ShouldBeFalse();
    }

    [Fact]
    public void A_link_is_good_only_for_its_own_photo_expiry_and_key()
    {
        var clock = new StubClock { UtcNow = new DateTime(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc) };
        var links = Links(clock);
        var (expires, signature) = Parse(links.Private(PhotoId));

        links.IsValid(Guid.NewGuid(), expires, signature).ShouldBeFalse();
        links.IsValid(PhotoId, expires + 600, signature).ShouldBeFalse();
        links.IsValid(PhotoId, expires, null).ShouldBeFalse();
        links.IsValid(PhotoId, null, signature).ShouldBeFalse();
        links.IsValid(PhotoId, long.MaxValue, signature).ShouldBeFalse();

        Links(clock, "another-signing-key-another-signing-key-0123456789").IsValid(PhotoId, expires, signature).ShouldBeFalse();
    }

    [Fact]
    public void The_local_store_refuses_keys_that_could_leave_its_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"upbazaar-photo-test-{Guid.NewGuid():N}");

        try
        {
            var store = new LocalDiskPhotoStore(folder);

            Should.Throw<ArgumentException>(() => store.OpenAsync("..\\secrets.json", default));
            Should.Throw<ArgumentException>(() => store.OpenAsync("../secrets.json", default));
            Should.Throw<ArgumentException>(() => store.OpenAsync("C:secrets", default));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static PhotoLinks Links(IClock clock, string key = "unit-test-signing-key-unit-test-signing-key-0123") =>
        new(JwtSettings.Resolve(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = key }).Build(),
                new TestEnvironment()),
            clock);

    private static (long Expires, string Signature) Parse(string url)
    {
        var query = url[(url.IndexOf('?', StringComparison.Ordinal) + 1)..]
            .Split('&')
            .Select(p => p.Split('='))
            .ToDictionary(p => p[0], p => p[1]);

        return (long.Parse(query["expires"], System.Globalization.CultureInfo.InvariantCulture), query["signature"]);
    }

    private sealed class StubClock : IClock
    {
        public DateTime UtcNow { get; set; }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "UPBazaar.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
