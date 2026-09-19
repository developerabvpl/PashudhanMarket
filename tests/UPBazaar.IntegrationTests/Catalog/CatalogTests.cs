using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Contracts.Events;
using UPBazaar.SharedKernel.Outbox;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Catalog;

[Collection(ApiCollection.Name)]
public sealed class CatalogTests(ApiFixture fixture)
{
    private static readonly Uri AdminProducts = new("/api/v1/admin/catalog/products", UriKind.Relative);
    private static readonly Uri AdminCategories = new("/api/v1/admin/catalog/categories", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task A_draft_is_invisible_to_shoppers_until_it_is_published()
    {
        var admin = await AdminClientAsync();
        var draft = await CreateProductAsync(admin, await CreateCategoryAsync(admin));

        var anonymous = fixture.CreateClient();

        (await anonymous.GetAsync(ProductUri(draft.Id))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(anonymous, $"search={draft.Sku}")).TotalCount.ShouldBe(0);

        // Staff with catalogue access see it all along.
        (await admin.GetAsync(ProductUri(draft.Id))).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await admin.PostAsync(new Uri($"{AdminProducts}/{draft.Id}/publish", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var published = await anonymous.GetFromJsonAsync<ProductDto>(ProductUri(draft.Id));
        published!.Status.ShouldBe("Active");
        (await ListAsync(anonymous, $"search={draft.Sku}")).TotalCount.ShouldBe(1);
    }

    [DatabaseFact]
    public async Task Shoppers_can_browse_without_signing_in_but_cannot_change_anything()
    {
        var anonymous = fixture.CreateClient();

        (await anonymous.GetAsync(new Uri("/api/v1/catalog/categories", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await anonymous.PostAsJsonAsync(AdminCategories, new { name = "Nope" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var buyer = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile());

        (await fixture.CreateAuthenticatedClient(buyer.AccessToken)
                .PostAsJsonAsync(AdminCategories, new { name = "Nope" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [DatabaseFact]
    public async Task A_duplicate_sku_is_a_conflict_whatever_its_case()
    {
        var admin = await AdminClientAsync();
        var category = await CreateCategoryAsync(admin);
        var product = await CreateProductAsync(admin, category);

        var response = await admin.PostAsJsonAsync(AdminProducts, ProductBody(product.Sku.ToLowerInvariant(), category));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task Changing_a_published_price_raises_an_event_and_a_draft_price_does_not()
    {
        var admin = await AdminClientAsync();
        var category = await CreateCategoryAsync(admin);
        var product = await CreateProductAsync(admin, category);

        (await UpdatePriceAsync(admin, product, category, 150m)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PriceEventsForAsync(product.Id)).ShouldBe(0);

        await admin.PostAsync(new Uri($"{AdminProducts}/{product.Id}/publish", UriKind.Relative), null);

        (await UpdatePriceAsync(admin, product, category, 175m)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PriceEventsForAsync(product.Id)).ShouldBe(1);
    }

    [DatabaseFact]
    public async Task An_archived_product_disappears_and_can_no_longer_be_edited()
    {
        var admin = await AdminClientAsync();
        var category = await CreateCategoryAsync(admin);
        var product = await CreateProductAsync(admin, category);

        await admin.PostAsync(new Uri($"{AdminProducts}/{product.Id}/publish", UriKind.Relative), null);

        (await admin.DeleteAsync(new Uri($"{AdminProducts}/{product.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await fixture.CreateClient().GetAsync(ProductUri(product.Id)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await UpdatePriceAsync(admin, product, category, 999m)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task A_top_level_category_lists_its_childrens_products()
    {
        var admin = await AdminClientAsync();
        var parent = await CreateCategoryAsync(admin);
        var child = await CreateCategoryAsync(admin, parent.Id);
        var product = await CreateProductAsync(admin, child);

        await admin.PostAsync(new Uri($"{AdminProducts}/{product.Id}/publish", UriKind.Relative), null);

        var page = await ListAsync(fixture.CreateClient(), $"categoryId={parent.Id}");

        page.Items.ShouldContain(p => p.Id == product.Id);
    }

    [DatabaseFact]
    public async Task A_category_cannot_be_moved_beneath_its_own_child()
    {
        var admin = await AdminClientAsync();
        var parent = await CreateCategoryAsync(admin);
        var child = await CreateCategoryAsync(admin, parent.Id);

        var response = await admin.PutAsJsonAsync(
            new Uri($"{AdminCategories}/{parent.Id}", UriKind.Relative),
            new { name = parent.Name, parentId = child.Id });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task A_price_with_fractions_of_a_paisa_is_rejected()
    {
        var admin = await AdminClientAsync();
        var category = await CreateCategoryAsync(admin);

        var response = await admin.PostAsJsonAsync(AdminProducts, new
        {
            sku = NewSku(),
            name = "Too precise",
            price = 10.005m,
            sellerId = Guid.NewGuid(),
            categoryId = category.Id,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await _auth.SignInAsSuperAdminAsync();

        return fixture.CreateAuthenticatedClient(tokens.AccessToken);
    }

    private static async Task<CategoryDto> CreateCategoryAsync(HttpClient admin, Guid? parentId = null)
    {
        var response = await admin.PostAsJsonAsync(
            AdminCategories,
            new { name = $"Category {Guid.NewGuid():N}", parentId });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CategoryDto>())!;
    }

    private static async Task<ProductDto> CreateProductAsync(HttpClient admin, CategoryDto category)
    {
        var response = await admin.PostAsJsonAsync(AdminProducts, ProductBody(NewSku(), category));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var product = (await response.Content.ReadFromJsonAsync<ProductDto>())!;
        product.Status.ShouldBe("Draft");

        return product;
    }

    private static object ProductBody(string sku, CategoryDto category) => new
    {
        sku,
        name = "Cow Dung Diya, pack of 12",
        brand = "Test Gaushala",
        price = 120m,
        sellerId = Guid.NewGuid(),
        categoryId = category.Id,
        onHandQuantity = 10,
    };

    private static Task<HttpResponseMessage> UpdatePriceAsync(
        HttpClient admin,
        ProductDto product,
        CategoryDto category,
        decimal price) =>
        admin.PutAsJsonAsync(
            new Uri($"{AdminProducts}/{product.Id}", UriKind.Relative),
            new { name = product.Name, brand = product.Brand, price, categoryId = category.Id });

    private static async Task<PagedList<ProductSummaryDto>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedList<ProductSummaryDto>>(
            new Uri($"/api/v1/catalog/products?{query}", UriKind.Relative)))!;

    private async Task<int> PriceEventsForAsync(Guid productId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();
        var type = typeof(ProductPriceChangedDomainEvent).FullName!;
        var id = productId.ToString();

        return await dbContext.Set<OutboxMessage>()
            .AsNoTracking()
            .CountAsync(m => m.Type.Contains(type) && m.Payload.Contains(id));
    }

    private static Uri ProductUri(Guid productId) =>
        new($"/api/v1/catalog/products/{productId}", UriKind.Relative);

    private static string NewSku() => $"TST-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
}
