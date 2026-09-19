using System.Text;

namespace UPBazaar.Modules.Catalog.Domain;

/// <summary>Turns a display name into a URL segment.</summary>
internal static class Slug
{
    /// <summary>Longest slug stored. Long product titles are cut rather than rejected.</summary>
    public const int MaxLength = 80;

    /// <summary>
    /// Lower-case ASCII letters and digits joined by single hyphens.
    ///
    /// Anything else - Devanagari included - is dropped rather than transliterated. A name made
    /// entirely of such characters falls back to <paramref name="fallback"/>, so a slug is never
    /// empty.
    /// </summary>
    public static string From(string name, string fallback)
    {
        var builder = new StringBuilder(name.Length);
        var pendingHyphen = false;

        foreach (var c in name.ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (pendingHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(c);
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var slug = builder.Length > MaxLength
            ? builder.ToString(0, MaxLength).TrimEnd('-')
            : builder.ToString();

        return slug.Length == 0 ? fallback.ToLowerInvariant() : slug;
    }
}
