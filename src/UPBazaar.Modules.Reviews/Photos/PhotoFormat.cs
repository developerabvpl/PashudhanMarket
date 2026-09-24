namespace UPBazaar.Modules.Reviews.Photos;

/// <summary>
/// Recognises the image formats reviews accept, from the file's first bytes.
///
/// The type a browser declares for an upload is whatever the file's name suggests, so it is not
/// trusted: a script renamed to .jpg must not be stored and later served as an image.
/// </summary>
public static class PhotoFormat
{
    /// <summary>Bytes needed to tell the formats apart.</summary>
    public const int HeaderLength = 12;

    /// <summary>Largest photo accepted. Phone cameras produce 2 to 4 MB.</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>The content type for these leading bytes, or null if they are not a JPEG, PNG or WebP.</summary>
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (header.Length >= 8 && header[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        // RIFF....WEBP
        if (header.Length >= 12
            && header[..4].SequenceEqual("RIFF"u8)
            && header[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    /// <summary>The file extension stored for a content type, so files on disk open by double-click.</summary>
    public static string ExtensionFor(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, "Not a review photo type."),
    };
}
