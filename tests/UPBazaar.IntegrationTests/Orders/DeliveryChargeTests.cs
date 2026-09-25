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
using UPBazaar.Modules.Sellers.Domain;
using UPBazaar.Modules.Settlements.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Orders;

/// <summary>
/// The delivery charge: a flat charge on orders below the free-delivery value, collected with
/// the goods, and earned by the seller whose parcel carried it - even if the buyer sends the goods
/// back.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DeliveryChargeTests(ApiFixture fixture)
{
    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task The_rule_is_published_for_the_storefront()
    {
        using var _ = fixture.WithDeliveryCharge(49m, 499m);

        var rule = await fixture.CreateClient().GetFromJsonAsync<DeliveryChargeDto>(
            new Uri("/api/v1/orders/delivery-charge", UriKind.Relative));

        rule.ShouldBe(new DeliveryChargeDto(49m, 499m, "INR"));
    }

    [DatabaseFact]
    public async Task A_small_order_pays_the_charge_which_the_seller_earns_and_keeps_after_a_return()
    {
        using var _ = fixture.WithDeliveryCharge(49m, 499m);

        var admin = await AdminClientAsync();
        var sellerId = await SellerAsync(admin);
        var (buyer, order, shipment) = await DeliveredAsync(admin, sellerId, price: 75m, quantity: 2);

        order.Subtotal.ShouldBe(150m);
        order.ShippingFee.ShouldBe(49m);
        order.Total.ShouldBe(199m);
        shipment.CodAmount.ShouldBe(199m);

        var earnings = await EarningsAsync(admin, sellerId);
        earnings.Single(e => e.Kind == "Sale").GrossAmount.ShouldBe(150m);

        var delivery = earnings.Single(e => e.Kind == "Delivery");
        delivery.GrossAmount.ShouldBe(49m);
        delivery.CommissionAmount.ShouldBe(0m);

        // The buyer sends the goods back: the sale is undone, the delivery is not.
        var part = order.Parts.Single();
        (await buyer.PostAsJsonAsync(
                new Uri($"/api/v1/orders/{order.Id}/parts/{part.Id}/return", UriKind.Relative),
                new { reason = "Damaged", refundUpiId = "asha.devi@okicici" }))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync(
                new Uri($"/api/v1/admin/orders/{order.Id}/parts/{part.Id}/return-decision", UriKind.Relative),
                new { approve = true }))
            .EnsureSuccessStatusCode();
        await ProcessOutboxAsync();

        earnings = await EarningsAsync(admin, sellerId);
        earnings.Single(e => e.Kind == "Sale").Status.ShouldBe("Cancelled");
        earnings.Single(e => e.Kind == "Delivery").Status.ShouldBe("Accruing");
    }

    [DatabaseFact]
    public async Task An_order_worth_the_free_delivery_value_is_delivered_free()
    {
        using var _ = fixture.WithDeliveryCharge(49m, 499m);

        var admin = await AdminClientAsync();
        var sellerId = await SellerAsync(admin);
        var (_, order, shipment) = await DeliveredAsync(admin, sellerId, price: 249.50m, quantity: 2);

        order.ShippingFee.ShouldBe(0m);
        order.Total.ShouldBe(499m);
        shipment.CodAmount.ShouldBe(499m);
        (await EarningsAsync(admin, sellerId)).ShouldHaveSingleItem().Kind.ShouldBe("Sale");
    }

    private static async Task<IReadOnlyList<EarningDto>> EarningsAsync(HttpClient admin, Guid sellerId) =>
        (await admin.GetFromJsonAsync<PagedList<EarningDto>>(
            new Uri($"/api/v1/admin/settlements/earnings?sellerId={sellerId}", UriKind.Relative)))!.Items;

    /// <summary>One product, bought cash on delivery by a new buyer, packed and delivered.</summary>
    private async Task<(HttpClient Buyer, OrderDto Order, ShipmentDto Shipment)> DeliveredAsync(
        HttpClient admin,
        Guid sellerId,
        decimal price,
        int quantity)
    {
        (await admin.PutAsJsonAsync(new Uri("/api/v1/admin/shipping/pickup-locations", UriKind.Relative), new { sellerId, name = "Delivery Gaushala" }))
            .EnsureSuccessStatusCode();

        var product = await CreateProductAsync(admin, sellerId, price);
        var buyer = fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile())).AccessToken);

        (await buyer.PutAsJsonAsync(new Uri($"/api/v1/cart/items/{product.Id}", UriKind.Relative), new { quantity }))
            .EnsureSuccessStatusCode();

        var placed = await buyer.PostAsJsonAsync(new Uri("/api/v1/orders", UriKind.Relative), new
        {
            paymentMethod = "CashOnDelivery",
            deliveryAddress = new { fullName = "Asha Devi", mobile = "9876543210", line1 = "12 Gaushala Road", city = "Lucknow", state = "Uttar Pradesh", pincode = "226024" },
        });
        placed.StatusCode.ShouldBe(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var order = (await placed.Content.ReadFromJsonAsync<OrderDto>())!;

        var packed = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/admin/shipping/orders/{order.Id}/parts/{order.Parts.Single().Id}/pack", UriKind.Relative),
            new { parcel = new { weightGrams = 300, lengthCm = 20m, breadthCm = 15m, heightCm = 10m } });
        packed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var shipment = (await packed.Content.ReadFromJsonAsync<ShipmentDto>())!;

        await CourierAsync(shipment.Awb!, "PICKED UP");
        await CourierAsync(shipment.Awb!, "DELIVERED");
        await ProcessOutboxAsync();

        return (buyer, order, shipment);
    }

    /// <summary>An approved seller, with an owner account so the seller is complete.</summary>
    private async Task<Guid> SellerAsync(HttpClient admin)
    {
        var id = Guid.NewGuid();

        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

            dbContext.Add(Seller.Seed(
                id,
                new SellerApplication(
                    "Delivery Gaushala", null, "9000000000", null, "5 Dairy Lane", null, "Lucknow", "Uttar Pradesh",
                    "226001", "Delivery Gaushala", null, "AAAAA0000A", "Delivery Gaushala", "112233445566", "SBIN0001234"),
                DateTime.UtcNow));

            await dbContext.SaveChangesAsync();
        }

        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile(), "Seller Owner");
        var me = await fixture.CreateAuthenticatedClient(tokens.AccessToken).GetFromJsonAsync<UserDto>(new Uri("/api/v1/users/me", UriKind.Relative));

        (await admin.PostAsJsonAsync(new Uri($"/api/v1/admin/sellers/{id}/owner", UriKind.Relative), new { ownerUserId = me!.Id }))
            .EnsureSuccessStatusCode();

        return id;
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

    private static async Task<ProductDto> CreateProductAsync(HttpClient admin, Guid sellerId, decimal price)
    {
        var category = await (await admin.PostAsJsonAsync(
                new Uri("/api/v1/admin/catalog/categories", UriKind.Relative),
                new { name = $"Category {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<CategoryDto>();

        var response = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/catalog/products", UriKind.Relative), new
        {
            sku = $"DLV-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            name = "Gobar Diya, pack of 12",
            price,
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
