using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Outbox;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Promotions.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Domain;
using UPBazaar.Modules.Settlements.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Promotions;

/// <summary>
/// Coupons: money off at checkout, collected and earned on as the coupon says - the platform's
/// coupons leave sellers paid in full, sellers' own and the campaigns they join come out of theirs.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CouponTests(ApiFixture fixture)
{
    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task A_platform_coupon_takes_money_off_and_leaves_the_seller_paid_in_full()
    {
        var admin = await AdminClientAsync();
        var (sellerId, seller) = await SellerAsync(admin);
        var code = await CreateAsync(admin, "/api/v1/admin/promotions/coupons", new { code = Code(), description = "Welcome", discountType = "Percent", value = 10m });
        var buyer = await BasketAsync(admin, sellerId);

        var preview = await PreviewAsync(buyer, code.ToLowerInvariant());
        preview.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await preview.Content.ReadFromJsonAsync<CouponPreviewDto>())!.Discount.ShouldBe(15m);

        var order = await PlaceAsync(buyer, code);
        order.Subtotal.ShouldBe(150m);
        order.Discount.ShouldBe(15m);
        order.Total.ShouldBe(135m);
        order.CouponCode.ShouldBe(code);
        order.Parts.Single().Lines.Single().Discount.ShouldBe(15m);

        // The seller sees the discount on their part, and that it is not theirs to bear.
        var sellerView = await seller.GetFromJsonAsync<SellerOrderDto>(new Uri($"/api/v1/seller/orders/{order.Id}", UriKind.Relative));
        sellerView!.Subtotal.ShouldBe(150m);
        sellerView.Discount.ShouldBe(15m);
        sellerView.DiscountFundedBy.ShouldBe("Platform");

        var shipment = await DeliverAsync(admin, order);
        shipment.CodAmount.ShouldBe(135m);

        (await EarningsAsync(admin, sellerId)).Single(e => e.Kind == "Sale").GrossAmount.ShouldBe(150m);
    }

    [DatabaseFact]
    public async Task A_seller_s_own_coupon_comes_out_of_what_they_are_paid()
    {
        var admin = await AdminClientAsync();
        var (sellerId, seller) = await SellerAsync(admin);
        var code = await CreateAsync(seller, "/api/v1/seller/promotions/coupons", new { code = Code(), description = "Shop sale", discountType = "Flat", value = 20m, fundedBy = "Platform" });

        var mine = await seller.GetFromJsonAsync<List<CouponDto>>(new Uri("/api/v1/seller/promotions/coupons", UriKind.Relative));
        mine!.ShouldHaveSingleItem().FundedBy.ShouldBe("Seller");

        var buyer = await BasketAsync(admin, sellerId);
        var order = await PlaceAsync(buyer, code);
        order.Total.ShouldBe(130m);

        var sellerView = await seller.GetFromJsonAsync<SellerOrderDto>(new Uri($"/api/v1/seller/orders/{order.Id}", UriKind.Relative));
        sellerView!.Discount.ShouldBe(20m);
        sellerView.DiscountFundedBy.ShouldBe("Seller");

        await DeliverAsync(admin, order);

        (await EarningsAsync(admin, sellerId)).Single(e => e.Kind == "Sale").GrossAmount.ShouldBe(130m);
    }

    [DatabaseFact]
    public async Task A_campaign_covers_only_the_sellers_who_join_it()
    {
        var admin = await AdminClientAsync();
        var (sellerId, seller) = await SellerAsync(admin);
        var code = await CreateAsync(admin, "/api/v1/admin/promotions/coupons", new { code = Code(), description = "Diwali", discountType = "Percent", value = 10m, fundedBy = "Seller" });
        var buyer = await BasketAsync(admin, sellerId);

        (await PreviewAsync(buyer, code)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var campaigns = await seller.GetFromJsonAsync<List<CouponDto>>(new Uri("/api/v1/seller/promotions/campaigns", UriKind.Relative));
        var campaign = campaigns!.Single(c => c.Code == code);
        campaign.Joined.ShouldBeFalse();

        var joined = await seller.PutAsJsonAsync(new Uri($"/api/v1/seller/promotions/campaigns/{campaign.Id}/joined", UriKind.Relative), new { joined = true });
        joined.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await joined.Content.ReadFromJsonAsync<CouponDto>())!.Joined.ShouldBeTrue();

        (await (await PreviewAsync(buyer, code)).Content.ReadFromJsonAsync<CouponPreviewDto>())!.Discount.ShouldBe(15m);
    }

    [DatabaseFact]
    public async Task A_buyer_uses_a_coupon_once_and_gets_it_back_if_the_order_is_cancelled()
    {
        var admin = await AdminClientAsync();
        var (sellerId, _) = await SellerAsync(admin);
        var code = await CreateAsync(admin, "/api/v1/admin/promotions/coupons", new { code = Code(), description = "Once", discountType = "Flat", value = 25m });
        var buyer = await BasketAsync(admin, sellerId);

        var order = await PlaceAsync(buyer, code);

        await AddToBasketAsync(admin, sellerId, buyer);
        (await PreviewAsync(buyer, code)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await buyer.PostAsJsonAsync(new Uri($"/api/v1/orders/{order.Id}/cancel", UriKind.Relative), new { reason = "Changed my mind." }))
            .EnsureSuccessStatusCode();
        await ProcessOutboxAsync();

        (await PreviewAsync(buyer, code)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [DatabaseFact]
    public async Task An_ended_coupon_fails_the_checkout_and_leaves_the_basket_as_it_was()
    {
        var admin = await AdminClientAsync();
        var (sellerId, _) = await SellerAsync(admin);
        var created = await admin.PostAsJsonAsync(
            new Uri("/api/v1/admin/promotions/coupons", UriKind.Relative),
            new { code = Code(), description = "Short-lived", discountType = "Flat", value = 10m });
        var coupon = (await created.Content.ReadFromJsonAsync<CouponDto>())!;
        var buyer = await BasketAsync(admin, sellerId);

        (await admin.PostAsync(new Uri($"/api/v1/admin/promotions/coupons/{coupon.Id}/end", UriKind.Relative), null)).EnsureSuccessStatusCode();

        (await PlaceResponseAsync(buyer, coupon.Code)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Nothing was used up: the order goes through without it.
        (await PlaceAsync(buyer, null)).Total.ShouldBe(150m);
    }

    [DatabaseFact]
    public async Task Platform_free_delivery_lifts_the_charge_and_the_seller_still_earns_it()
    {
        using var _ = fixture.WithDeliveryCharge(49m, 499m);

        var admin = await AdminClientAsync();
        var (sellerId, _) = await SellerAsync(admin);
        var code = await CreateAsync(admin, "/api/v1/admin/promotions/coupons", new { code = Code(), description = "Free delivery", discountType = "FreeDelivery", value = 0m });
        var buyer = await BasketAsync(admin, sellerId);

        var preview = (await (await PreviewAsync(buyer, code)).Content.ReadFromJsonAsync<CouponPreviewDto>())!;
        preview.Discount.ShouldBe(0m);
        preview.DeliveryDiscount.ShouldBe(49m);

        var order = await PlaceAsync(buyer, code);
        order.ShippingFee.ShouldBe(49m);
        order.DeliveryDiscount.ShouldBe(49m);
        order.Total.ShouldBe(150m);

        (await DeliverAsync(admin, order)).CodAmount.ShouldBe(150m);

        (await EarningsAsync(admin, sellerId)).Single(e => e.Kind == "Delivery").GrossAmount.ShouldBe(49m);
    }

    [DatabaseFact]
    public async Task A_seller_s_free_delivery_waives_their_delivery_earning()
    {
        using var _ = fixture.WithDeliveryCharge(49m, 499m);

        var admin = await AdminClientAsync();
        var (sellerId, seller) = await SellerAsync(admin);
        var code = await CreateAsync(seller, "/api/v1/seller/promotions/coupons", new { code = Code(), description = "Ships free", discountType = "FreeDelivery", value = 0m });
        var buyer = await BasketAsync(admin, sellerId);

        var order = await PlaceAsync(buyer, code);
        order.Total.ShouldBe(150m);

        await DeliverAsync(admin, order);

        (await EarningsAsync(admin, sellerId)).ShouldNotContain(e => e.Kind == "Delivery");
    }

    [DatabaseFact]
    public async Task Free_delivery_is_refused_when_delivery_is_free_anyway()
    {
        var admin = await AdminClientAsync();
        var (sellerId, _) = await SellerAsync(admin);
        var code = await CreateAsync(admin, "/api/v1/admin/promotions/coupons", new { code = Code(), description = "Free delivery", discountType = "FreeDelivery", value = 0m });
        var buyer = await BasketAsync(admin, sellerId);

        var preview = await PreviewAsync(buyer, code);
        preview.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await preview.Content.ReadAsStringAsync()).ShouldContain("delivery_already_free");

        (await PlaceResponseAsync(buyer, code)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static string Code() => $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private static async Task<string> CreateAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CouponDto>())!.Code;
    }

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient buyer, string code) =>
        buyer.PostAsJsonAsync(new Uri("/api/v1/promotions/coupons/preview", UriKind.Relative), new { code });

    private static Task<HttpResponseMessage> PlaceResponseAsync(HttpClient buyer, string? couponCode) =>
        buyer.PostAsJsonAsync(new Uri("/api/v1/orders", UriKind.Relative), new
        {
            paymentMethod = "CashOnDelivery",
            deliveryAddress = new { fullName = "Asha Devi", mobile = "9876543210", line1 = "12 Gaushala Road", city = "Lucknow", state = "Uttar Pradesh", pincode = "226024" },
            couponCode,
        });

    private static async Task<OrderDto> PlaceAsync(HttpClient buyer, string? couponCode)
    {
        var placed = await PlaceResponseAsync(buyer, couponCode);
        placed.StatusCode.ShouldBe(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());

        return (await placed.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    /// <summary>A new buyer with two of a 75-rupee product in their basket.</summary>
    private async Task<HttpClient> BasketAsync(HttpClient admin, Guid sellerId)
    {
        var buyer = fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile())).AccessToken);
        await AddToBasketAsync(admin, sellerId, buyer);

        return buyer;
    }

    private static async Task AddToBasketAsync(HttpClient admin, Guid sellerId, HttpClient buyer)
    {
        var product = await CreateProductAsync(admin, sellerId);

        (await buyer.PutAsJsonAsync(new Uri($"/api/v1/cart/items/{product.Id}", UriKind.Relative), new { quantity = 2 }))
            .EnsureSuccessStatusCode();
    }

    private async Task<ShipmentDto> DeliverAsync(HttpClient admin, OrderDto order)
    {
        var packed = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/admin/shipping/orders/{order.Id}/parts/{order.Parts.Single().Id}/pack", UriKind.Relative),
            new { parcel = new { weightGrams = 300, lengthCm = 20m, breadthCm = 15m, heightCm = 10m } });
        packed.StatusCode.ShouldBe(HttpStatusCode.OK, await packed.Content.ReadAsStringAsync());
        var shipment = (await packed.Content.ReadFromJsonAsync<ShipmentDto>())!;

        await CourierAsync(shipment.Awb!, "PICKED UP");
        await CourierAsync(shipment.Awb!, "DELIVERED");
        await ProcessOutboxAsync();

        return shipment;
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
                    "Coupon Gaushala", null, "9000000000", null, "5 Dairy Lane", null, "Lucknow", "Uttar Pradesh",
                    "226001", "Coupon Gaushala", null, "AAAAA0000A", "Coupon Gaushala", "112233445566", "SBIN0001234"),
                DateTime.UtcNow));

            await dbContext.SaveChangesAsync();
        }

        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile(), "Seller Owner");
        var me = await fixture.CreateAuthenticatedClient(tokens.AccessToken).GetFromJsonAsync<UserDto>(new Uri("/api/v1/users/me", UriKind.Relative));

        (await admin.PostAsJsonAsync(new Uri($"/api/v1/admin/sellers/{id}/owner", UriKind.Relative), new { ownerUserId = me!.Id }))
            .EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync(new Uri("/api/v1/admin/shipping/pickup-locations", UriKind.Relative), new { sellerId = id, name = "Coupon Gaushala" }))
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

    private static async Task<ProductDto> CreateProductAsync(HttpClient admin, Guid sellerId)
    {
        var category = await (await admin.PostAsJsonAsync(
                new Uri("/api/v1/admin/catalog/categories", UriKind.Relative),
                new { name = $"Category {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<CategoryDto>();

        var response = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/catalog/products", UriKind.Relative), new
        {
            sku = $"CPN-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
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
