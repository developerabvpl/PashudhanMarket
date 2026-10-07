using System.Text.RegularExpressions;

namespace UPBazaar.Api.Configuration;

/// <summary>
/// Decides which file of the packaged website answers an address.
///
/// These are the rules of <c>web/tools/deploy/web.config</c>, said again in code so the API can
/// hand out the storefront and the portals itself (see <see cref="SiteFilesSetup"/>). Keep the two
/// in step: a rule changed in one place only would make the same address behave differently
/// depending on how the site happens to be hosted.
///
/// Kept apart from the middleware and free of the file system, so every rule can be tested with
/// a plain list of file names.
/// </summary>
public static partial class SiteRoutes
{
    /// <summary>The empty shell that boots the storefront's router on whatever was asked for.</summary>
    public const string StorefrontShell = "/index.csr.html";

    /// <summary>The folders the portals are served from. The folder name is the URL path.</summary>
    private static readonly string[] Portals = ["seller", "admin"];

    /// <summary>
    /// Addresses the API answers itself. Listed rather than discovered, because the storefront's
    /// shell answers anything else that is not a file, and would otherwise answer these too.
    /// </summary>
    private static readonly string[] ApiPaths = ["/api", "/health", "/jobs", "/openapi", "/scalar", "/swagger"];

    /// <summary>
    /// Works out the answer for a request path.
    /// </summary>
    /// <param name="path">The request path, starting with a slash.</param>
    /// <param name="isFile">Whether a path names a file in the packaged site.</param>
    /// <returns>What the site does with the address; null when it is the API's to answer.</returns>
    public static SiteAnswer? Resolve(string path, Func<string, bool> isFile)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(isFile);

        if (ApiPaths.Any(own => IsUnder(path, own)))
        {
            return null;
        }

        // The application's own root route redirects to the catalogue. A real redirect is faster
        // than the meta refresh the prerenderer wrote, and is the answer a crawler looks for.
        if (path == "/")
        {
            return SiteAnswer.Redirect("/products");
        }

        var portal = Portals.FirstOrDefault(name => IsUnder(path, "/" + name));

        // Without its trailing slash a portal would still load, but a relative link in the page
        // would be worked out against the wrong folder.
        if (portal is not null && path.Length == portal.Length + 1)
        {
            return SiteAnswer.Redirect(path + "/");
        }

        if (isFile(path))
        {
            return SiteAnswer.Send(path);
        }

        // Something that looks like an asset and is not there stays a 404. Answering it with a
        // page would bury the real fault - a file that did not deploy - under a syntax error.
        if (AssetPath().IsMatch(path))
        {
            return SiteAnswer.NotFound;
        }

        // A portal draws every page in the browser: anything under its folder gets its own page.
        if (portal is not null)
        {
            return SiteAnswer.Send($"/{portal}/index.html");
        }

        // A prerendered page is a folder holding index.html, asked for with or without the slash.
        var prerendered = path.TrimEnd('/') + "/index.html";

        // The rest - /sign-in, /cart, /account - is drawn in the browser. The shell, not
        // index.html: that one is the root route's redirect and would bounce every refresh on
        // /sign-in over to the catalogue.
        return SiteAnswer.Send(isFile(prerendered) ? prerendered : StorefrontShell);
    }

    private static bool IsUnder(string path, string folder) =>
        path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
        && (path.Length == folder.Length || path[folder.Length] == '/');

    [GeneratedRegex(
        @"\.(js|mjs|css|map|ico|png|jpe?g|gif|svg|webp|avif|woff2?|ttf|eot|json|txt|xml|webmanifest)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AssetPath();
}

/// <summary>What the site does with an address: send one of its files, redirect, or 404.</summary>
/// <param name="File">Path of the file to send, from the site's root.</param>
/// <param name="RedirectTo">Address to redirect to, permanently.</param>
public readonly record struct SiteAnswer(string? File, string? RedirectTo)
{
    /// <summary>The address is the site's, and nothing is there.</summary>
    public static SiteAnswer NotFound => default;

    public static SiteAnswer Send(string file) => new(file, null);

    public static SiteAnswer Redirect(string address) => new(null, address);
}
