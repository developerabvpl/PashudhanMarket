using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Outbox;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Gateway;

namespace UPBazaar.IntegrationTests.Shipping;

/// <summary>
/// Packing, courier booking and tracking against the fake courier. The suite shares one database,
/// so every test uses its own seller and gives that seller its own pickup location, rather than
/// relying on - or changing - the platform warehouse other tests may have set.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ShippingTests(ApiFixture fixture)
{
    private static readonly Uri WebhookUri = new("/api/v1/shipping/webhooks/courier-tracking", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task Packing_works_the_parcel_out_from_product_packages_and_books_the_courier()
    {
        var admin = await AdminClientAsync();
        var seller = await SellerWithPickupAsync(admin, "Gaushala Lucknow");
        var diya = await CreateProductAsync(admin, 100m, seller, package: (250, 20m, 15m, 5m));
        var sabun = await CreateProductAsync(admin, 50m, seller, package: (120, 10m, 8m, 4m));
        var buyer = await BuyerClientAsync();

        await AddToCartAsync(buyer, diya.Id, 2);
        await AddToCartAsync(buyer, sabun.Id, 1);
        var order = await PlaceCodAsync(buyer);
        var part = order.Parts.Single();

        var suggestion = await admin.GetFromJsonAsync<ParcelSuggestionDto>(ParcelUri(order.Id, part.Id));
        suggestion!.Parcel.ShouldBe(new ParcelDto(2 * 250 + 120, 20m, 15m, 2 * 5m + 4m));
        suggestion.MissingSkus.ShouldBeEmpty();
        suggestion.PickupLocation.ShouldBe("Gaushala Lucknow");

        var shipment = await PackAsync(admin, order.Id, part.Id, parcel: null);

        shipment.Status.ShouldBe("PickupRequested");
        shipment.Awb.ShouldNotBeNullOrEmpty();
        shipment.CourierName.ShouldBe("Fake Express");
        shipment.PaymentMode.ShouldBe("COD");
        shipment.CodAmount.ShouldBe(250m);
        shipment.Parcel.WeightGrams.ShouldBe(620);

        (await OrderAsync(buyer, order.Id)).Parts.Single().Status.ShouldBe("Packed");

        // Packing again returns the same booking rather than a second consignment.
        (await PackAsync(admin, order.Id, part.Id, parcel: null)).Id.ShouldBe(shipment.Id);
    }

    [DatabaseFact]
    public async Task A_product_without_a_package_needs_the_parcel_entered_by_hand()
    {
        var admin = await AdminClientAsync();
        var seller = await SellerWithPickupAsync(admin, "Seller Kanpur");
        var product = await CreateProductAsync(admin, 80m, seller, package: null);
        var buyer = await BuyerClientAsync();

        await AddToCartAsync(buyer, product.Id, 1);
        var order = await PlaceCodAsync(buyer);
        var part = order.Parts.Single();

        var suggestion = await admin.GetFromJsonAsync<ParcelSuggestionDto>(ParcelUri(order.Id, part.Id));
        suggestion!.Parcel.ShouldBeNull();
        suggestion.MissingSkus.ShouldBe([product.Sku]);

        (await admin.PostAsJsonAsync(PackUri(order.Id, part.Id), new { })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var shipment = await PackAsync(admin, order.Id, part.Id, new ParcelDto(400, 25m, 20m, 10m));
        shipment.Parcel.ShouldBe(new ParcelDto(400, 25m, 20m, 10m));
    }

    [DatabaseFact]
    public async Task A_failed_booking_step_is_resumed_by_packing_again_without_booking_twice()
    {
        var admin = await AdminClientAsync();
        var seller = await SellerWithPickupAsync(admin, $"Seller {FakeCourierGateway.FailingPickupMarker}");
        var product = await CreateProductAsync(admin, 60m, seller, package: (100, 10m, 10m, 10m));
        var buyer = await BuyerClientAsync();

        await AddToCartAsync(buyer, product.Id, 1);
        var order = await PlaceCodAsync(buyer);
        var part = order.Parts.Single();

        var failed = await admin.PostAsJsonAsync(PackUri(order.Id, part.Id), new { });
        failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await OrderAsync(buyer, order.Id)).Parts.Single().Status.ShouldBe("Confirmed");

        var halfBooked = (await admin.GetFromJsonAsync<List<ShipmentDto>>(AdminShipmentsUri(order.Id)))!.Single();
        halfBooked.Status.ShouldBe("Booking");
        halfBooked.CarrierOrderId.ShouldNotBeNull();
        halfBooked.LastError.ShouldNotBeNull();

        var resumed = await PackAsync(admin, order.Id, part.Id, parcel: null);

        resumed.Id.ShouldBe(halfBooked.Id);
        resumed.CarrierOrderId.ShouldBe(halfBooked.CarrierOrderId);
        resumed.Status.ShouldBe("PickupRequested");
        resumed.LastError.ShouldBeNull();
    }

    [DatabaseFact]
    public async Task Courier_updates_move_the_part_to_shipped_then_delivered_and_repeats_are_harmless()
    {
        var (admin, buyer, order, shipment) = await BookedOrderAsync();

        (await PostWebhookAsync(Update(shipment.Awb!, "PICKED UP"), FakeCourierGateway.WebhookToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await OrderAsync(buyer, order.Id)).Parts.Single().Status.ShouldBe("Shipped");

        (await PostWebhookAsync(Update(shipment.Awb!, "DELIVERED"), FakeCourierGateway.WebhookToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PostWebhookAsync(Update(shipment.Awb!, "IN TRANSIT"), FakeCourierGateway.WebhookToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var delivered = await OrderAsync(buyer, order.Id);
        delivered.Status.ShouldBe("Completed");
        delivered.Parts.Single().Status.ShouldBe("Delivered");

        var tracked = (await buyer.GetFromJsonAsync<List<ShipmentDto>>(TrackUri(order.Id)))!.Single();
        tracked.Status.ShouldBe("Delivered");
        tracked.Events.Select(e => e.Status).ShouldBe(["PICKED UP", "DELIVERED", "IN TRANSIT"]);
        tracked.TrackingUrl.ShouldNotBeNull();

        // The buyer route shows only the caller's own orders, even to a super-admin who holds
        // every permission; staff read shipments through the admin route instead.
        (await admin.GetFromJsonAsync<List<ShipmentDto>>(TrackUri(order.Id)))!.ShouldBeEmpty();
        (await admin.GetFromJsonAsync<List<ShipmentDto>>(AdminShipmentsUri(order.Id)))!.ShouldHaveSingleItem();
    }

    [DatabaseFact]
    public async Task A_courier_update_without_the_token_is_refused_and_an_unknown_awb_is_ignored()
    {
        var (_, buyer, order, shipment) = await BookedOrderAsync();

        (await PostWebhookAsync(Update(shipment.Awb!, "DELIVERED"), token: null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await PostWebhookAsync(Update(shipment.Awb!, "DELIVERED"), "guess")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await PostWebhookAsync(Update("NOT-OURS-1", "DELIVERED"), FakeCourierGateway.WebhookToken)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        (await OrderAsync(buyer, order.Id)).Parts.Single().Status.ShouldBe("Packed");
    }

    [DatabaseFact]
    public async Task Cancelling_a_booked_part_calls_the_courier_off()
    {
        var (admin, _, order, _) = await BookedOrderAsync();

        (await admin.PostAsJsonAsync(
                new Uri($"/api/v1/admin/orders/{order.Id}/parts/{order.Parts.Single().Id}/cancel", UriKind.Relative),
                new { reason = "Buyer changed address" }))
            .EnsureSuccessStatusCode();

        await ProcessOutboxAsync();

        (await admin.GetFromJsonAsync<List<ShipmentDto>>(AdminShipmentsUri(order.Id)))!.Single().Status
            .ShouldBe("Cancelled");
    }

    [DatabaseFact]
    public async Task Another_buyer_sees_no_shipments_for_an_order_that_is_not_theirs()
    {
        var (_, _, order, _) = await BookedOrderAsync();
        var other = await BuyerClientAsync();

        (await other.GetFromJsonAsync<List<ShipmentDto>>(TrackUri(order.Id)))!.ShouldBeEmpty();
    }

    [DatabaseFact]
    public async Task A_seller_without_a_pickup_location_ships_from_the_platform_warehouse()
    {
        var admin = await AdminClientAsync();

        (await admin.PutAsJsonAsync(PickupUri, new { sellerId = (Guid?)null, name = "UP Bazaar Lucknow Hub" }))
            .EnsureSuccessStatusCode();

        var product = await CreateProductAsync(admin, 70m, Guid.NewGuid(), package: (100, 10m, 10m, 10m));
        var buyer = await BuyerClientAsync();

        await AddToCartAsync(buyer, product.Id, 1);
        var order = await PlaceCodAsync(buyer);

        (await PackAsync(admin, order.Id, order.Parts.Single().Id, parcel: null)).PickupLocation
            .ShouldBe("UP Bazaar Lucknow Hub");

        var locations = await admin.GetFromJsonAsync<List<PickupLocationDto>>(PickupUri);
        locations!.First().SellerId.ShouldBeNull();
    }

    private async Task<(HttpClient Admin, HttpClient Buyer, OrderDto Order, ShipmentDto Shipment)> BookedOrderAsync()
    {
        var admin = await AdminClientAsync();
        var seller = await SellerWithPickupAsync(admin, "Seller Varanasi");
        var product = await CreateProductAsync(admin, 90m, seller, package: (300, 20m, 20m, 10m));
        var buyer = await BuyerClientAsync();

        await AddToCartAsync(buyer, product.Id, 1);
        var order = await PlaceCodAsync(buyer);

        return (admin, buyer, order, await PackAsync(admin, order.Id, order.Parts.Single().Id, parcel: null));
    }

    private async Task<HttpClient> AdminClientAsync() =>
        fixture.CreateAuthenticatedClient((await _auth.SignInAsSuperAdminAsync()).AccessToken);

    private async Task<HttpClient> BuyerClientAsync() =>
        fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile())).AccessToken);

    private async Task<HttpResponseMessage> PostWebhookAsync(string body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (token is not null)
        {
            request.Headers.Add("x-api-key", token);
        }

        return await fixture.CreateClient().SendAsync(request);
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

    private static async Task<Guid> SellerWithPickupAsync(HttpClient admin, string pickupName)
    {
        var seller = Guid.NewGuid();

        (await admin.PutAsJsonAsync(PickupUri, new { sellerId = seller, name = pickupName })).EnsureSuccessStatusCode();

        return seller;
    }

    private static async Task<ShipmentDto> PackAsync(HttpClient admin, Guid orderId, Guid partId, ParcelDto? parcel)
    {
        var response = await admin.PostAsJsonAsync(PackUri(orderId, partId), new { parcel });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<ShipmentDto>())!;
    }

    /// <summary>The fields of Shiprocket's tracking payload that are read, as Shiprocket names them.</summary>
    private static string Update(string awb, string status) =>
        JsonSerializer.Serialize(new { awb, current_status = status, current_timestamp = "22 09 2026 10:00:00" });

    private static async Task<OrderDto> PlaceCodAsync(HttpClient buyer)
    {
        var response = await buyer.PostAsJsonAsync(new Uri("/api/v1/orders", UriKind.Relative), new
        {
            paymentMethod = "CashOnDelivery",
            deliveryAddress = new
            {
                fullName = "Asha Devi",
                mobile = "9876543210",
                line1 = "12 Gaushala Road",
                line2 = "Aliganj",
                city = "Lucknow",
                state = "Uttar Pradesh",
                pincode = "226024",
            },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    private static async Task<OrderDto> OrderAsync(HttpClient buyer, Guid orderId) =>
        (await buyer.GetFromJsonAsync<OrderDto>(new Uri($"/api/v1/orders/{orderId}", UriKind.Relative)))!;

    private static async Task AddToCartAsync(HttpClient buyer, Guid productId, int quantity) =>
        (await buyer.PutAsJsonAsync(new Uri($"/api/v1/cart/items/{productId}", UriKind.Relative), new { quantity }))
        .EnsureSuccessStatusCode();

    private static async Task<ProductDto> CreateProductAsync(
        HttpClient admin,
        decimal price,
        Guid sellerId,
        (int Weight, decimal Length, decimal Breadth, decimal Height)? package)
    {
        var category = await (await admin.PostAsJsonAsync(
                new Uri("/api/v1/admin/catalog/categories", UriKind.Relative),
                new { name = $"Category {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<CategoryDto>();

        var response = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/catalog/products", UriKind.Relative), new
        {
            sku = $"SHP-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
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

        if (package is { } p)
        {
            var packaged = await admin.PutAsJsonAsync(
                new Uri($"/api/v1/admin/catalog/products/{product.Id}/package", UriKind.Relative),
                new { weightGrams = p.Weight, lengthCm = p.Length, breadthCm = p.Breadth, heightCm = p.Height });

            packaged.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await packaged.Content.ReadFromJsonAsync<ProductDto>())!.Package.ShouldNotBeNull();
        }

        return product;
    }

    private static readonly Uri PickupUri = new("/api/v1/admin/shipping/pickup-locations", UriKind.Relative);

    private static Uri ParcelUri(Guid orderId, Guid partId) =>
        new($"/api/v1/admin/shipping/orders/{orderId}/parts/{partId}/parcel", UriKind.Relative);

    private static Uri PackUri(Guid orderId, Guid partId) =>
        new($"/api/v1/admin/shipping/orders/{orderId}/parts/{partId}/pack", UriKind.Relative);

    private static Uri AdminShipmentsUri(Guid orderId) =>
        new($"/api/v1/admin/shipping/orders/{orderId}/shipments", UriKind.Relative);

    private static Uri TrackUri(Guid orderId) => new($"/api/v1/shipping/orders/{orderId}/shipments", UriKind.Relative);
}
