using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace UPBazaar.Api.Configuration;

/// <summary>
/// Serves the website - the storefront at /, the seller portal at /seller/ and the admin portal
/// at /admin/ - from the API's own <c>wwwroot</c> folder, when there is one.
///
/// This is what makes the deployment one folder and one IIS website: the browser gets its pages
/// and calls /api on the same site, with no second website in front and no URL Rewrite or
/// Application Request Routing to install. The package is put together by
/// <c>web/tools/scripts/package-server.mjs</c>.
///
/// Without the folder nothing is added, so a development run, the tests, and a deployment that
/// keeps the website on a separate site (<c>web/tools/deploy/web.config</c>) are unchanged.
/// </summary>
public static class SiteFilesSetup
{
    public const string Folder = "wwwroot";

    /// <summary>
    /// An hour, for the reason given in web.config: short enough that a redeploy reaches people
    /// the same day, and the pages and the hashed assets share a folder, so one value covers both.
    /// </summary>
    private const string CacheControl = "public, max-age=3600";

    public static WebApplication UseSiteFiles(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var root = Path.Combine(app.Environment.ContentRootPath, Folder);

        if (!File.Exists(Path.Combine(root, SiteRoutes.StorefrontShell.TrimStart('/'))))
        {
            return app;
        }

        var files = new PhysicalFileProvider(root);

        app.Use((context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                return next(context);
            }

            var answer = SiteRoutes.Resolve(
                context.Request.Path.Value ?? "/",
                path => files.GetFileInfo(path) is { Exists: true, IsDirectory: false });

            if (answer is { RedirectTo: { } address })
            {
                context.Response.Redirect(address + context.Request.QueryString, permanent: true);

                return Task.CompletedTask;
            }

            if (answer == SiteAnswer.NotFound)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;

                return Task.CompletedTask;
            }

            if (answer is { File: { } file })
            {
                context.Request.Path = file;

                // Routing has already run and matched the catch-all that answers 404 for an
                // unknown address, and the file middleware stands aside for a matched endpoint.
                // This request is the site's, so let go of it.
                context.SetEndpoint(null);
            }

            return next(context);
        });

        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".mjs"] = "text/javascript";
        contentTypes.Mappings[".avif"] = "image/avif";
        contentTypes.Mappings[".webmanifest"] = "application/manifest+json";

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = response =>
            {
                var headers = response.Context.Response.Headers;

                headers.CacheControl = CacheControl;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            },
        });

        return app;
    }
}
