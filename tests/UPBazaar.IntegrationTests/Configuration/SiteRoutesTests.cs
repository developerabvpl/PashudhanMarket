using UPBazaar.Api.Configuration;

namespace UPBazaar.IntegrationTests.Configuration;

/// <summary>One site answers the storefront, both portals and the API, each from its own path.</summary>
public sealed class SiteRoutesTests
{
    private static readonly HashSet<string> Files =
    [
        "/index.html",
        "/index.csr.html",
        "/main-ABC.js",
        "/products/index.html",
        "/products/ghee/index.html",
        "/seller/index.html",
        "/seller/main-DEF.js",
        "/admin/index.html",
    ];

    [Theory]
    [InlineData("/main-ABC.js", "/main-ABC.js")]
    [InlineData("/products", "/products/index.html")]
    [InlineData("/products/", "/products/index.html")]
    [InlineData("/products/ghee", "/products/ghee/index.html")]
    [InlineData("/sign-in", "/index.csr.html")]
    [InlineData("/orders/42", "/index.csr.html")]
    [InlineData("/seller/", "/seller/index.html")]
    [InlineData("/seller/orders", "/seller/index.html")]
    [InlineData("/seller/main-DEF.js", "/seller/main-DEF.js")]
    [InlineData("/admin/sellers/7", "/admin/index.html")]
    [InlineData("/sellers", "/index.csr.html")]
    public void Sends_the_file_that_owns_the_address(string path, string file)
    {
        Resolve(path).ShouldBe(SiteAnswer.Send(file));
    }

    [Theory]
    [InlineData("/", "/products")]
    [InlineData("/seller", "/seller/")]
    [InlineData("/admin", "/admin/")]
    public void Redirects_the_root_and_a_portal_without_its_slash(string path, string address)
    {
        Resolve(path).ShouldBe(SiteAnswer.Redirect(address));
    }

    [Theory]
    [InlineData("/api/v1/catalog/categories")]
    [InlineData("/health")]
    [InlineData("/jobs")]
    [InlineData("/scalar/v1")]
    public void Leaves_the_api_alone(string path)
    {
        Resolve(path).ShouldBeNull();
    }

    [Theory]
    [InlineData("/main-GONE.js")]
    [InlineData("/seller/main-GONE.js")]
    [InlineData("/media/missing.webp")]
    public void Answers_a_missing_asset_with_not_found_rather_than_a_page(string path)
    {
        Resolve(path).ShouldBe(SiteAnswer.NotFound);
    }

    private static SiteAnswer? Resolve(string path) => SiteRoutes.Resolve(path, Files.Contains);
}
