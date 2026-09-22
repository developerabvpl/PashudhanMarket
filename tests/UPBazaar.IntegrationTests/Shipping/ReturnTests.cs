using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Outbox;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Gateway;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Shipping;

/// <summary>
/// Parcels the courier could not deliver (RTO), from the courier turning round to the seller
/// deciding whether the goods go back on sale.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReturnTests(ApiFixture fixture)
{
    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task An_undelivered_parcel_comes_back_and_goes_back_on_sale_once_inspected_as_good()
    {
        var (admin, buyer, product, order, shipment) = await ShippedAsync("CashOnDelivery", quantity: 2);

        await CourierAsync(shipment.Awb!, "RTO INITIATED");
        (await OrderAsync(buyer, order.Id)).Parts.Single().Status.ShouldBe("Returning");

        await CourierAsync(shipment.Awb!, "RTO DELIVERED");

        var returned = await OrderAsync(buyer, order.Id);
        returned.Parts.Single().Status.ShouldBe("Returned");
        returned.Status.ShouldBe("Cancelled");
        returned.CancellationReason.ShouldBe(Order.CouldNotDeliver);

        // Committed at checkout, so gone from stock until someone says the goods are fine.
        (await StockAsync(admin, product.Id)).OnHandQuantity.ShouldBe(8);

        var inspected = await InspectAsync(admin, order, "Good");
        inspected.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await inspected.Content.ReadFromJsonAsync<OrderDto>())!.Parts.Single().ReturnCondition.ShouldBe("Good");

        (await StockAsync(admin, product.Id)).OnHandQuantity.ShouldBe(10);

        // Once only: a second inspection would restock the same goods twice.
        (await InspectAsync(admin, order, "Good")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await StockAsync(admin, product.Id)).OnHandQuantity.ShouldBe(10);
    }

    [DatabaseFact]
    public async Task A_damaged_return_does_not_go_back_on_sale()
    {
        var (admin, _, product, order, shipment) = await ShippedAsync("CashOnDelivery", quantity: 3);

        await CourierAsync(shipment.Awb!, "RTO DELIVERED");

        (await InspectAsync(admin, order, "Damaged", "Box crushed in transit")).EnsureSuccessStatusCode();

        (await StockAsync(admin, product.Id)).OnHandQuantity.ShouldBe(7);
    }

    [DatabaseFact]
    public async Task A_parcel_cannot_be_inspected_before_it_is_back()
    {
        var (admin, _, _, order, shipment) = await ShippedAsync("CashOnDelivery", quantity: 1);

        await CourierAsync(shipment.Awb!, "RTO INITIATED");

        (await InspectAsync(admin, order, "Good")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task A_prepaid_return_is_owed_back_only_once_the_goods_are_back()
    {
        var (_, _, _, order, shipment) = await ShippedAsync("Online", quantity: 2);

        await CourierAsync(shipment.Awb!, "RTO INITIATED");
        await ProcessOutboxAsync();
        (await RefundsForAsync(order.Id)).ShouldBeEmpty();

        await CourierAsync(shipment.Awb!, "RTO DELIVERED");
        await ProcessOutboxAsync();

        var refund = (await RefundsForAsync(order.Id)).ShouldHaveSingleItem();
        refund.Amount.ShouldBe(2 * 75m);
        refund.Status.ShouldBe("Due");
    }

    [DatabaseFact]
    public async Task A_delivered_parcel_is_not_turned_round_by_a_late_rto_update()
    {
        var (_, buyer, _, order, shipment) = await ShippedAsync("CashOnDelivery", quantity: 1);

        await CourierAsync(shipment.Awb!, "DELIVERED");
        await CourierAsync(shipment.Awb!, "RTO INITIATED");

        var delivered = await OrderAsync(buyer, order.Id);
        delivered.Parts.Single().Status.ShouldBe("Delivered");
        delivered.Status.ShouldBe("Completed");
    }

    /// <summary>A product with 10 in stock, bought and booked with the courier, and picked up.</summary>
    private async Task<(HttpClient Admin, HttpClient Buyer, ProductDto Product, OrderDto Order, ShipmentDto Shipment)> ShippedAsync(
        string paymentMethod,
        int quantity)
    {
        var admin = await AdminClientAsync();
        var seller = Guid.NewGuid();

        (await admin.PutAsJsonAsync(new Uri("/api/v1/admin/shipping/pickup-locations", UriKind.Relative), new { sellerId = seller, name = "Return Test Gaushala" }))
            .EnsureSuccessStatusCode();

        var product = await CreateProductAsync(admin, price: 75m, seller);
        var buyer = fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile())).AccessToken);

        (await buyer.PutAsJsonAsync(new Uri($"/api/v1/cart/items/{product.Id}", UriKind.Relative), new { quantity }))
            .EnsureSuccessStatusCode();

        var order = await PlaceAsync(buyer, paymentMethod);

        if (paymentMethod == "Online")
        {
            await PayAsync(buyer, order.Id);
        }

        var packed = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/admin/shipping/orders/{order.Id}/parts/{order.Parts.Single().Id}/pack", UriKind.Relative),
            new { parcel = new { weightGrams = 300, lengthCm = 20m, breadthCm = 15m, heightCm = 10m } });

        packed.StatusCode.ShouldBe(HttpStatusCode.OK, await packed.Content.ReadAsStringAsync());

        var shipment = (await packed.Content.ReadFromJsonAsync<ShipmentDto>())!;
        await CourierAsync(shipment.Awb!, "PICKED UP");

        return (admin, buyer, product, order, shipment);
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

    private static Task<HttpResponseMessage> InspectAsync(HttpClient admin, OrderDto order, string condition, string? note = null) =>
        admin.PostAsJsonAsync(
            new Uri($"/api/v1/admin/orders/{order.Id}/parts/{order.Parts.Single().Id}/return-inspection", UriKind.Relative),
            new { condition, note });

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

    private async Task<IReadOnlyList<RefundDto>> RefundsForAsync(Guid orderId)
    {
        var admin = await AdminClientAsync();
        var refunds = await admin.GetFromJsonAsync<PagedList<RefundDto>>(new Uri("/api/v1/admin/payments/refunds?pageSize=100", UriKind.Relative));

        return [.. refunds!.Items.Where(r => r.OrderId == orderId)];
    }

    private static async Task PayAsync(HttpClient buyer, Guid orderId)
    {
        var session = await (await buyer.PostAsync(new Uri($"/api/v1/payments/orders/{orderId}/checkout", UriKind.Relative), null))
            .Content.ReadFromJsonAsync<CheckoutSessionDto>();
        var paymentId = $"pay_{Guid.NewGuid():N}"[..20];

        (await buyer.PostAsJsonAsync(new Uri("/api/v1/payments/razorpay/verify", UriKind.Relative), new
        {
            gatewayOrderId = session!.GatewayOrderId,
            gatewayPaymentId = paymentId,
            signature = RazorpaySignature.ForPayment(session.GatewayOrderId, paymentId, FakeGateway.KeySecret),
        })).EnsureSuccessStatusCode();
    }

    private static async Task<OrderDto> PlaceAsync(HttpClient buyer, string paymentMethod)
    {
        var response = await buyer.PostAsJsonAsync(new Uri("/api/v1/orders", UriKind.Relative), new
        {
            paymentMethod,
            deliveryAddress = new
            {
                fullName = "Asha Devi",
                mobile = "9876543210",
                line1 = "12 Gaushala Road",
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

    private static async Task<StockLevelDto> StockAsync(HttpClient admin, Guid productId) =>
        (await admin.GetFromJsonAsync<StockDetailDto>(new Uri($"/api/v1/admin/inventory/stock/{productId}", UriKind.Relative)))!.Level;

    private static async Task<ProductDto> CreateProductAsync(HttpClient admin, decimal price, Guid sellerId)
    {
        var category = await (await admin.PostAsJsonAsync(
                new Uri("/api/v1/admin/catalog/categories", UriKind.Relative),
                new { name = $"Category {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<CategoryDto>();

        var response = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/catalog/products", UriKind.Relative), new
        {
            sku = $"RTO-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
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
