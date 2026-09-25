using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Outbox;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Cart.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Promotions.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Domain;
using UPBazaar.Modules.Settlements.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Catalog;

/// <summary>
/// Sales: a seller's lower price for a while. It is simply the price while it runs - what carts,
/// orders, coupons and the seller's earnings all go by - and staff can end any sale.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProductSaleTests(ApiFixture fixture)
{
    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task A_sale_price_is_what_the_buyer_pays_and_the_seller_is_paid_on()
    {
        var admin = await AdminClientAsync();
        var (sellerId, seller) = await SellerAsync(admin);
        var product = await CreateProductAsync(admin, sellerId);

        var onSale = await SetSaleAsync(seller, product.Id, new { salePrice = 60m, endsAtUtc = DateTime.UtcNow.AddDays(2) });
        onSale.StatusCode.ShouldBe(HttpStatusCode.OK, await onSale.Content.ReadAsStringAsync());

        var shown = await fixture.CreateClient().GetFromJsonAsync<ProductDto>(new Uri($"/api/v1/catalog/products/{product.Id}", UriKind.Relative));
        shown!.Price.ShouldBe(75m);
        shown.CurrentPrice.ShouldBe(60m);
        shown.Sale!.IsRunning.ShouldBeTrue();

        // A platform coupon stacks on the sale price.
        var coupon = await admin.PostAsJsonAsync(
            new Uri("/api/v1/admin/promotions/coupons", UriKind.Relative),
            new { code = $"S{Guid.NewGuid():N}"[..12].ToUpperInvariant(), description = "On top", discountType = "Percent", value = 10m });
        var code = (await coupon.Content.ReadFromJsonAsync<CouponDto>())!.Code;

        var buyer = await BuyerWithAsync(product.Id);
        var order = await PlaceAsync(buyer, code);
        order.Subtotal.ShouldBe(120m);
        order.Discount.ShouldBe(12m);
        order.Total.ShouldBe(108m);

        await DeliverAsync(admin, order);

        (await EarningsAsync(admin, sellerId)).Single(e => e.Kind == "Sale").GrossAmount.ShouldBe(120m);
    }

    [DatabaseFact]
    public async Task Staff_can_end_a_sale_and_carts_holding_it_see_the_price_change()
    {
        var admin = await AdminClientAsync();
        var (sellerId, seller) = await SellerAsync(admin);
        var product = await CreateProductAsync(admin, sellerId);
        (await SetSaleAsync(seller, product.Id, new { salePrice = 60m, endsAtUtc = DateTime.UtcNow.AddDays(2) })).EnsureSuccessStatusCode();
        var buyer = await BuyerWithAsync(product.Id);

        var ended = await admin.DeleteAsync(new Uri($"/api/v1/admin/catalog/products/{product.Id}/sale", UriKind.Relative));
        ended.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ended.Content.ReadFromJsonAsync<ProductDto>())!.Sale.ShouldBeNull();

        var cart = await buyer.GetFromJsonAsync<CartDto>(new Uri("/api/v1/cart", UriKind.Relative));
        var line = cart!.Lines.Single();
        line.UnitPrice.ShouldBe(75m);
        line.Problem.ShouldBe("PriceChanged");
    }

    [DatabaseFact]
    public async Task A_sale_must_cut_the_price_and_only_its_seller_can_set_it()
    {
        var admin = await AdminClientAsync();
        var (sellerId, seller) = await SellerAsync(admin);
        var (_, other) = await SellerAsync(admin);
        var product = await CreateProductAsync(admin, sellerId);

        (await SetSaleAsync(seller, product.Id, new { salePrice = 75m, endsAtUtc = DateTime.UtcNow.AddDays(2) }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SetSaleAsync(other, product.Id, new { salePrice = 60m, endsAtUtc = DateTime.UtcNow.AddDays(2) }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nor can the regular price drop to meet a sale that is set.
        (await SetSaleAsync(seller, product.Id, new { salePrice = 60m, endsAtUtc = DateTime.UtcNow.AddDays(2) })).EnsureSuccessStatusCode();
        (await seller.PutAsJsonAsync(new Uri($"/api/v1/seller/catalog/products/{product.Id}/price", UriKind.Relative), new { price = 55m }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static Task<HttpResponseMessage> SetSaleAsync(HttpClient seller, Guid productId, object body) =>
        seller.PutAsJsonAsync(new Uri($"/api/v1/seller/catalog/products/{productId}/sale", UriKind.Relative), body);

    /// <summary>A new buyer with two of the product in their basket.</summary>
    private async Task<HttpClient> BuyerWithAsync(Guid productId)
    {
        var buyer = fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile())).AccessToken);

        (await buyer.PutAsJsonAsync(new Uri($"/api/v1/cart/items/{productId}", UriKind.Relative), new { quantity = 2 }))
            .EnsureSuccessStatusCode();

        return buyer;
    }

    private static async Task<OrderDto> PlaceAsync(HttpClient buyer, string? couponCode)
    {
        var placed = await buyer.PostAsJsonAsync(new Uri("/api/v1/orders", UriKind.Relative), new
        {
            paymentMethod = "CashOnDelivery",
            deliveryAddress = new { fullName = "Asha Devi", mobile = "9876543210", line1 = "12 Gaushala Road", city = "Lucknow", state = "Uttar Pradesh", pincode = "226024" },
            couponCode,
        });
        placed.StatusCode.ShouldBe(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());

        return (await placed.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    private async Task DeliverAsync(HttpClient admin, OrderDto order)
    {
        var packed = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/admin/shipping/orders/{order.Id}/parts/{order.Parts.Single().Id}/pack", UriKind.Relative),
            new { parcel = new { weightGrams = 300, lengthCm = 20m, breadthCm = 15m, heightCm = 10m } });
        packed.StatusCode.ShouldBe(HttpStatusCode.OK, await packed.Content.ReadAsStringAsync());
        var shipment = (await packed.Content.ReadFromJsonAsync<ShipmentDto>())!;

        await CourierAsync(shipment.Awb!, "PICKED UP");
        await CourierAsync(shipment.Awb!, "DELIVERED");
        await ProcessOutboxAsync();
    }

    private static async Task<IReadOnlyList<EarningDto>> EarningsAsync(HttpClient admin, Guid sellerId) =>
        (await admin.GetFromJsonAsync<PagedList<EarningDto>>(
            new Uri($"/api/v1/admin/settlements/earnings?sellerId={sellerId}", UriKind.Relative)))!.Items;

    /// <summary>An approved seller with an owner signed in, and a pickup location.</summary>
    private async Task<(Guid Id, HttpClient Client)> SellerAsync(HttpClient admin)
    {
        var id = Guid.NewGuid();

        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

            dbContext.Add(Seller.Seed(
                id,
                new SellerApplication(
                    "Sale Gaushala", null, "9000000000", null, "5 Dairy Lane", null, "Lucknow", "Uttar Pradesh",
                    "226001", "Sale Gaushala", null, "AAAAA0000A", "Sale Gaushala", "112233445566", "SBIN0001234"),
                DateTime.UtcNow));

            await dbContext.SaveChangesAsync();
        }

        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile(), "Seller Owner");
        var me = await fixture.CreateAuthenticatedClient(tokens.AccessToken).GetFromJsonAsync<UserDto>(new Uri("/api/v1/users/me", UriKind.Relative));

        (await admin.PostAsJsonAsync(new Uri($"/api/v1/admin/sellers/{id}/owner", UriKind.Relative), new { ownerUserId = me!.Id }))
            .EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync(new Uri("/api/v1/admin/shipping/pickup-locations", UriKind.Relative), new { sellerId = id, name = "Sale Gaushala" }))
            .EnsureSuccessStatusCode();

        var refreshed = await (await _auth.RefreshAsync(tokens.RefreshToken)).Content.ReadFromJsonAsync<AuthTokensDto>();

        return (id, fixture.CreateAuthenticatedClient(refreshed!.AccessToken));
    }

    private async Task<HttpClient> AdminClientAsync() =>
        fixture.CreateAuthenticatedClient((await _auth.SignInAsSuperAdminAsync()).AccessToken);

    private async Task CourierAsync(string awb, string status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/shipping/webhooks/courier-tracking", UriKind.Relative))
        {
            Content = new StringContent(JsonSerializer.Serialize(new { awb, current_status = status }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", FakeCourierGateway.WebhookToken);

        (await fixture.CreateClient().SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task ProcessOutboxAsync()
    {
        for (var batch = 0; batch < 100; batch++)
        {
            using var scope = fixture.CreateScope();

            if (await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessAsync() == 0)
            {
                return;
            }
        }
    }

    /// <summary>A published 75-rupee product of the seller's, ten in stock.</summary>
    private static async Task<ProductDto> CreateProductAsync(HttpClient admin, Guid sellerId)
    {
        var category = await (await admin.PostAsJsonAsync(
                new Uri("/api/v1/admin/catalog/categories", UriKind.Relative),
                new { name = $"Category {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<CategoryDto>();

        var response = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/catalog/products", UriKind.Relative), new
        {
            sku = $"SAL-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            name = "Gobar Diya, pack of 12",
            price = 75m,
            sellerId,
            categoryId = category!.Id,
            onHandQuantity = 10,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var product = (await response.Content.ReadFromJsonAsync<ProductDto>())!;

        (await admin.PostAsync(new Uri($"/api/v1/admin/catalog/products/{product.Id}/publish", UriKind.Relative), null))
            .EnsureSuccessStatusCode();

        return product;
    }
}
