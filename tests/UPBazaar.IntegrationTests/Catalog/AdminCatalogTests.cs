using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Domain;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Catalog;

/// <summary>The staff catalogue screens: the product list, the seller picker and category upkeep.</summary>
[Collection(ApiCollection.Name)]
public sealed class AdminCatalogTests(ApiFixture fixture)
{
    private static readonly Uri AdminProducts = new("/api/v1/admin/catalog/products", UriKind.Relative);
    private static readonly Uri AdminCategories = new("/api/v1/admin/catalog/categories", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task The_staff_list_names_seller_and_category_and_filters_by_status()
    {
        var admin = await AdminClientAsync();
        var (sellerId, shopName) = await ApprovedSellerAsync();
        var category = await CreateCategoryAsync(admin);
        var product = await CreateProductAsync(admin, category, sellerId);

        var page = await ListAsync(admin, $"sellerId={sellerId}");

        var row = page.Items.ShouldHaveSingleItem();
        row.Id.ShouldBe(product.Id);
        row.SellerName.ShouldBe(shopName);
        row.CategoryName.ShouldBe(category.Name);
        row.Status.ShouldBe("Draft");
        row.AvailableQuantity.ShouldBe(10);
        row.HasPackage.ShouldBeFalse();

        (await ListAsync(admin, $"sellerId={sellerId}&status=Active")).TotalCount.ShouldBe(0);
    }

    [DatabaseFact]
    public async Task A_queue_can_be_worked_oldest_first()
    {
        var admin = await AdminClientAsync();
        var (sellerId, _) = await ApprovedSellerAsync();
        var category = await CreateCategoryAsync(admin);
        var first = await CreateProductAsync(admin, category, sellerId);
        var second = await CreateProductAsync(admin, category, sellerId);

        (await ListAsync(admin, $"sellerId={sellerId}")).Items.Select(p => p.Id).ShouldBe([second.Id, first.Id]);
        (await ListAsync(admin, $"sellerId={sellerId}&oldestFirst=true")).Items.Select(p => p.Id).ShouldBe([first.Id, second.Id]);
    }

    [DatabaseFact]
    public async Task The_staff_list_and_seller_picker_are_closed_to_shoppers()
    {
        var anonymous = fixture.CreateClient();
        (await anonymous.GetAsync(AdminProducts)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var buyer = fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile())).AccessToken);
        (await buyer.GetAsync(AdminProducts)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await buyer.GetAsync(new Uri("/api/v1/admin/catalog/sellers", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [DatabaseFact]
    public async Task Approved_sellers_are_offered_for_new_listings()
    {
        var admin = await AdminClientAsync();
        var (sellerId, shopName) = await ApprovedSellerAsync();

        var sellers = await admin.GetFromJsonAsync<IReadOnlyList<SellerNameDto>>(
            new Uri("/api/v1/admin/catalog/sellers", UriKind.Relative));

        sellers!.ShouldContain(new SellerNameDto(sellerId, shopName));
    }

    [DatabaseFact]
    public async Task Only_an_empty_category_can_be_deleted()
    {
        var admin = await AdminClientAsync();
        var (sellerId, _) = await ApprovedSellerAsync();

        var withProduct = await CreateCategoryAsync(admin);
        var product = await CreateProductAsync(admin, withProduct, sellerId);

        // Archived still counts: orders point at it, and it keeps its shelf.
        await admin.DeleteAsync(new Uri($"{AdminProducts}/{product.Id}", UriKind.Relative));
        (await DeleteCategoryAsync(admin, withProduct.Id)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var parent = await CreateCategoryAsync(admin);
        await CreateCategoryAsync(admin, parent.Id);
        (await DeleteCategoryAsync(admin, parent.Id)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var empty = await CreateCategoryAsync(admin);
        (await DeleteCategoryAsync(admin, empty.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var categories = await admin.GetFromJsonAsync<IReadOnlyList<CategoryDto>>(
            new Uri("/api/v1/catalog/categories", UriKind.Relative));
        categories!.ShouldNotContain(c => c.Id == empty.Id);

        (await DeleteCategoryAsync(admin, empty.Id)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<HttpClient> AdminClientAsync() =>
        fixture.CreateAuthenticatedClient((await _auth.SignInAsSuperAdminAsync()).AccessToken);

    /// <summary>An approved seller with a shop name no other test uses.</summary>
    private async Task<(Guid Id, string ShopName)> ApprovedSellerAsync()
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();
        var id = Guid.NewGuid();
        var shopName = $"Test Gaushala {id:N}"[..30];

        dbContext.Add(Seller.Seed(
            id,
            new SellerApplication(
                shopName, null, "9000000000", null, "To be completed", null, "Lucknow", "Uttar Pradesh",
                "226001", shopName, null, "AAAAA0000A", shopName, "000000000", "SBIN0000000"),
            DateTime.UtcNow));

        await dbContext.SaveChangesAsync();

        return (id, shopName);
    }

    private static async Task<CategoryDto> CreateCategoryAsync(HttpClient admin, Guid? parentId = null)
    {
        var response = await admin.PostAsJsonAsync(AdminCategories, new { name = $"Category {Guid.NewGuid():N}", parentId });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CategoryDto>())!;
    }

    private static async Task<ProductDto> CreateProductAsync(HttpClient admin, CategoryDto category, Guid sellerId)
    {
        var response = await admin.PostAsJsonAsync(AdminProducts, new
        {
            sku = $"TST-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            name = "Cow Dung Diya, pack of 12",
            price = 120m,
            sellerId,
            categoryId = category.Id,
            onHandQuantity = 10,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    private static Task<HttpResponseMessage> DeleteCategoryAsync(HttpClient admin, Guid categoryId) =>
        admin.DeleteAsync(new Uri($"{AdminCategories}/{categoryId}", UriKind.Relative));

    private static async Task<PagedList<AdminProductSummaryDto>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedList<AdminProductSummaryDto>>(
            new Uri($"{AdminProducts}?{query}", UriKind.Relative)))!;
}
