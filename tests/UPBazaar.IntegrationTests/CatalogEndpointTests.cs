using System.Net;
using System.Net.Http.Json;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Contracts.Permissions;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CatalogEndpointTests(ApiFixture fixture)
{
    [DatabaseFact]
    public async Task Creating_a_product_returns_201_and_the_product_can_be_read_back()
    {
        var categoryId = await fixture.CreateCategoryAsync("Sarees");
        var client = fixture.CreateClientWith(
            CatalogPermissions.ProductsWrite,
            CatalogPermissions.ProductsRead);

        var response = await client.PostAsJsonAsync("/api/catalog/products", NewProduct(categoryId));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<ProductDto>();
        created.ShouldNotBeNull();
        created.Status.ShouldBe("Draft");
        created.OnHandQuantity.ShouldBe(25);

        var fetched = await client.GetFromJsonAsync<ProductDto>($"/api/catalog/products/{created.Id}");

        fetched.ShouldNotBeNull();
        fetched.Sku.ShouldBe(created.Sku);
        fetched.Category.Id.ShouldBe(categoryId);
    }

    [DatabaseFact]
    public async Task A_duplicate_sku_is_rejected_with_409()
    {
        var categoryId = await fixture.CreateCategoryAsync("Sarees");
        var client = fixture.CreateClientWith(CatalogPermissions.ProductsWrite);
        var request = NewProduct(categoryId);

        (await client.PostAsJsonAsync("/api/catalog/products", request))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync("/api/catalog/products", request);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task An_invalid_price_is_rejected_with_400_and_field_level_errors()
    {
        var categoryId = await fixture.CreateCategoryAsync("Sarees");
        var client = fixture.CreateClientWith(CatalogPermissions.ProductsWrite);

        var response = await client.PostAsJsonAsync(
            "/api/catalog/products",
            NewProduct(categoryId) with { Price = 0m });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Price");
    }

    [DatabaseFact]
    public async Task A_published_product_shows_up_in_the_active_listing()
    {
        var categoryId = await fixture.CreateCategoryAsync("Sarees");
        var client = fixture.CreateClientWith(
            CatalogPermissions.ProductsWrite,
            CatalogPermissions.ProductsRead);

        var request = NewProduct(categoryId);
        var created = await (await client.PostAsJsonAsync("/api/catalog/products", request))
            .Content.ReadFromJsonAsync<ProductDto>();

        var publish = await client.PostAsync($"/api/catalog/products/{created!.Id}/publish", null);
        publish.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var listing = await client.GetFromJsonAsync<PagedList<ProductSummaryDto>>(
            $"/api/catalog/products?page=1&pageSize=50&search={request.Sku}");

        listing.ShouldNotBeNull();
        listing.Items.ShouldContain(p => p.Id == created.Id && p.Status == "Active");
    }

    [DatabaseFact]
    public async Task Adjusting_stock_updates_the_on_hand_quantity()
    {
        var categoryId = await fixture.CreateCategoryAsync("Sarees");
        var client = fixture.CreateClientWith(
            CatalogPermissions.ProductsWrite,
            CatalogPermissions.ProductsRead,
            CatalogPermissions.StockWrite);

        var created = await (await client.PostAsJsonAsync("/api/catalog/products", NewProduct(categoryId)))
            .Content.ReadFromJsonAsync<ProductDto>();

        var adjust = await client.PutAsJsonAsync(
            $"/api/catalog/products/{created!.Id}/stock",
            new { onHand = 7 });

        adjust.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var fetched = await client.GetFromJsonAsync<ProductDto>($"/api/catalog/products/{created.Id}");
        fetched!.OnHandQuantity.ShouldBe(7);
    }

    [DatabaseFact]
    public async Task An_unknown_product_returns_404()
    {
        var client = fixture.CreateClientWith(CatalogPermissions.ProductsRead);

        var response = await client.GetAsync($"/api/catalog/products/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task A_caller_without_the_permission_is_refused()
    {
        var withWrongPermission = fixture.CreateClientWith(CatalogPermissions.ProductsRead);
        var anonymous = fixture.CreateClient();

        (await withWrongPermission.PostAsJsonAsync("/api/catalog/products", NewProduct(Guid.NewGuid())))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await anonymous.GetAsync($"/api/catalog/products/{Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static CreateProductRequest NewProduct(Guid categoryId) => new(
        SellerId: Guid.NewGuid(),
        Sku: $"UPB-{Guid.NewGuid():N}"[..20],
        Name: "Banarasi Silk Saree",
        Description: "Handwoven silk saree from Varanasi.",
        CategoryId: categoryId,
        Price: 4599.00m,
        Currency: "INR",
        InitialStock: 25);

    private sealed record CreateProductRequest(
        Guid SellerId,
        string Sku,
        string Name,
        string? Description,
        Guid CategoryId,
        decimal Price,
        string Currency,
        int InitialStock);
}
