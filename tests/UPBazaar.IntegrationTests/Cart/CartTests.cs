using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Cart.Contracts;
using UPBazaar.Modules.Cart.Contracts.Dtos;
using UPBazaar.Modules.Cart.Domain;
using UPBazaar.Modules.Catalog.Contracts.Dtos;

namespace UPBazaar.IntegrationTests.Cart;

[Collection(ApiCollection.Name)]
public sealed class CartTests(ApiFixture fixture)
{
    private static readonly Uri CartUri = new("/api/v1/cart", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task A_cart_needs_a_signed_in_buyer()
    {
        (await fixture.CreateClient().GetAsync(CartUri)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task A_buyer_adds_products_and_sees_live_totals()
    {
        var admin = await AdminClientAsync();
        var diya = await CreateProductAsync(admin, price: 120m, stock: 10);
        var kande = await CreateProductAsync(admin, price: 45.50m, stock: 10);
        var buyer = await BuyerClientAsync();

        await SetAsync(buyer, diya.Id, 2);
        var cart = await SetAsync(buyer, kande.Id, 3);

        cart.Lines.Count.ShouldBe(2);
        cart.Subtotal.ShouldBe(376.50m);
        cart.ItemCount.ShouldBe(5);
        cart.CanCheckOut.ShouldBeTrue();
        cart.Lines.ShouldAllBe(l => l.Problem == null);
    }

    [DatabaseFact]
    public async Task Each_buyer_sees_only_their_own_cart()
    {
        var admin = await AdminClientAsync();
        var product = await CreateProductAsync(admin, price: 10m, stock: 10);

        await SetAsync(await BuyerClientAsync(), product.Id, 4);

        var other = await (await BuyerClientAsync()).GetFromJsonAsync<CartDto>(CartUri);
        other!.Lines.ShouldBeEmpty();
    }

    [DatabaseFact]
    public async Task Adding_more_than_is_in_stock_is_refused_but_lowering_is_always_allowed()
    {
        var admin = await AdminClientAsync();
        var product = await CreateProductAsync(admin, price: 10m, stock: 5);
        var buyer = await BuyerClientAsync();

        (await buyer.PutAsJsonAsync(ItemUri(product.Id), new { quantity = 6 })).StatusCode
            .ShouldBe(HttpStatusCode.Conflict);

        await SetAsync(buyer, product.Id, 5);

        // Stock falls under the buyer's feet; the line is flagged, and lowering it fixes it.
        (await admin.PostAsJsonAsync(
                new Uri($"/api/v1/admin/inventory/stock/{product.Id}/write-offs", UriKind.Relative),
                new { quantity = 3, reason = "Broken" }))
            .EnsureSuccessStatusCode();

        var flagged = await buyer.GetFromJsonAsync<CartDto>(CartUri);
        flagged!.Lines.Single().Problem.ShouldBe("InsufficientStock");
        flagged.CanCheckOut.ShouldBeFalse();

        var fixedCart = await SetAsync(buyer, product.Id, 2);
        fixedCart.Lines.Single().Problem.ShouldBeNull();
    }

    [DatabaseFact]
    public async Task A_product_that_is_not_on_sale_cannot_be_added()
    {
        var admin = await AdminClientAsync();
        var draft = await CreateProductAsync(admin, price: 10m, stock: 5, publish: false);

        (await (await BuyerClientAsync()).PutAsJsonAsync(ItemUri(draft.Id), new { quantity = 1 }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task A_price_change_is_flagged_until_the_buyer_accepts_it()
    {
        var admin = await AdminClientAsync();
        var product = await CreateProductAsync(admin, price: 100m, stock: 5);
        var buyer = await BuyerClientAsync();

        await SetAsync(buyer, product.Id, 1);
        await RepriceAsync(admin, product, 110m);

        var flagged = await buyer.GetFromJsonAsync<CartDto>(CartUri);
        var line = flagged!.Lines.Single();
        line.Problem.ShouldBe("PriceChanged");
        line.PriceWhenAdded.ShouldBe(100m);
        line.UnitPrice.ShouldBe(110m);
        flagged.CanCheckOut.ShouldBeFalse();

        var accepted = await (await buyer.PostAsync(new Uri("/api/v1/cart/acknowledge-prices", UriKind.Relative), null))
            .Content.ReadFromJsonAsync<CartDto>();

        accepted!.Lines.Single().Problem.ShouldBeNull();
        accepted.Subtotal.ShouldBe(110m);
    }

    [DatabaseFact]
    public async Task An_archived_product_stays_in_the_cart_flagged_and_out_of_the_total()
    {
        var admin = await AdminClientAsync();
        var kept = await CreateProductAsync(admin, price: 10m, stock: 5);
        var withdrawn = await CreateProductAsync(admin, price: 99m, stock: 5);
        var buyer = await BuyerClientAsync();

        await SetAsync(buyer, kept.Id, 1);
        await SetAsync(buyer, withdrawn.Id, 1);

        await admin.DeleteAsync(new Uri($"/api/v1/admin/catalog/products/{withdrawn.Id}", UriKind.Relative));

        var cart = await buyer.GetFromJsonAsync<CartDto>(CartUri);

        cart!.Lines.Single(l => l.ProductId == withdrawn.Id).Problem.ShouldBe("Unavailable");
        cart.Subtotal.ShouldBe(10m);
        cart.CanCheckOut.ShouldBeFalse();
    }

    [DatabaseFact]
    public async Task A_guest_basket_merges_in_adding_up_capping_and_skipping_what_is_not_on_sale()
    {
        var admin = await AdminClientAsync();
        var both = await CreateProductAsync(admin, price: 10m, stock: 200);
        var guestOnly = await CreateProductAsync(admin, price: 20m, stock: 10);
        var draft = await CreateProductAsync(admin, price: 30m, stock: 10, publish: false);
        var buyer = await BuyerClientAsync();

        await SetAsync(buyer, both.Id, 60);

        var response = await buyer.PostAsJsonAsync(new Uri("/api/v1/cart/merge", UriKind.Relative), new
        {
            lines = new object[]
            {
                new { productId = both.Id, quantity = 60 },
                new { productId = guestOnly.Id, quantity = 2 },
                new { productId = draft.Id, quantity = 1 },
                new { productId = Guid.NewGuid(), quantity = 1 },
            },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var merged = await response.Content.ReadFromJsonAsync<CartMergeResultDto>();

        merged!.Cart.Lines.Single(l => l.ProductId == both.Id).Quantity.ShouldBe(ShoppingCart.MaxQuantityPerLine);
        merged.Cart.Lines.Single(l => l.ProductId == guestOnly.Id).Quantity.ShouldBe(2);
        merged.Skipped.Count.ShouldBe(2);
        merged.Skipped.ShouldContain(draft.Id);
    }

    [DatabaseFact]
    public async Task Checkout_takes_a_clean_cart_and_refuses_one_with_problems()
    {
        var admin = await AdminClientAsync();
        var product = await CreateProductAsync(admin, price: 50m, stock: 5);
        var (buyer, buyerId) = await BuyerWithIdAsync();

        await SetAsync(buyer, product.Id, 2);

        var lines = await CheckoutLinesAsync(buyerId);
        lines.IsSuccess.ShouldBeTrue();
        lines.Value.Single().ShouldBe(new CheckoutLineDto(product.Id, 2, 50m, "INR", product.SellerId));

        await RepriceAsync(admin, product, 55m);

        (await CheckoutLinesAsync(buyerId)).Error.ShouldBe(CartErrors.NotReadyForCheckout);
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await _auth.SignInAsSuperAdminAsync();

        return fixture.CreateAuthenticatedClient(tokens.AccessToken);
    }

    private async Task<HttpClient> BuyerClientAsync() => (await BuyerWithIdAsync()).Client;

    private async Task<(HttpClient Client, Guid BuyerId)> BuyerWithIdAsync()
    {
        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile());
        var client = fixture.CreateAuthenticatedClient(tokens.AccessToken);
        var me = await client.GetFromJsonAsync<Modules.Identity.Contracts.Dtos.UserDto>(
            new Uri("/api/v1/users/me", UriKind.Relative));

        return (client, me!.Id);
    }

    private async Task<SharedKernel.Results.Result<IReadOnlyList<CheckoutLineDto>>> CheckoutLinesAsync(Guid buyerId)
    {
        using var scope = fixture.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ICartService>().GetCheckoutLinesAsync(buyerId, default);
    }

    private static async Task<CartDto> SetAsync(HttpClient buyer, Guid productId, int quantity)
    {
        var response = await buyer.PutAsJsonAsync(ItemUri(productId), new { quantity });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<CartDto>())!;
    }

    private static async Task<ProductDto> CreateProductAsync(HttpClient admin, decimal price, int stock, bool publish = true)
    {
        var category = await (await admin.PostAsJsonAsync(
                new Uri("/api/v1/admin/catalog/categories", UriKind.Relative),
                new { name = $"Category {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<CategoryDto>();

        var response = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/catalog/products", UriKind.Relative), new
        {
            sku = $"CRT-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            name = "Panchgavya Sabun, 100g",
            price,
            sellerId = Guid.NewGuid(),
            categoryId = category!.Id,
            onHandQuantity = stock,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var product = (await response.Content.ReadFromJsonAsync<ProductDto>())!;

        if (publish)
        {
            (await admin.PostAsync(new Uri($"/api/v1/admin/catalog/products/{product.Id}/publish", UriKind.Relative), null))
                .EnsureSuccessStatusCode();
        }

        return product;
    }

    private static async Task RepriceAsync(HttpClient admin, ProductDto product, decimal price) =>
        (await admin.PutAsJsonAsync(
            new Uri($"/api/v1/admin/catalog/products/{product.Id}", UriKind.Relative),
            new { name = product.Name, price, categoryId = product.Category.Id }))
        .EnsureSuccessStatusCode();

    private static Uri ItemUri(Guid productId) => new($"/api/v1/cart/items/{productId}", UriKind.Relative);
}
