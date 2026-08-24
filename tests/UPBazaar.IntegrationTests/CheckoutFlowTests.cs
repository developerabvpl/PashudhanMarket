using System.Net;
using System.Net.Http.Json;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Contracts.Permissions;
using UPBazaar.Modules.Ordering.Contracts.Dtos;
using UPBazaar.Modules.Ordering.Contracts.Permissions;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Contracts.Permissions;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Contracts.Permissions;

namespace UPBazaar.IntegrationTests;

/// <summary>
/// Covers the path a real order takes across all four modules: catalog reserves stock,
/// ordering places the order, payments captures via webhook, shipping books off the outbox.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CheckoutFlowTests(ApiFixture fixture)
{
    // Stable for the lifetime of one test, so a retry really is the same request.
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _sellerId = Guid.NewGuid();

    [DatabaseFact]
    public async Task Checkout_places_an_order_holds_stock_and_opens_a_payment()
    {
        var product = await PublishProductAsync(stock: 10, price: 1000m);
        var client = fixture.CreateClientWith(
            OrderingPermissions.OrdersWrite,
            OrderingPermissions.OrdersRead);

        var response = await Checkout(client, product.Id, quantity: 2, key: Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        order.ShouldNotBeNull();
        order.Status.ShouldBe("AwaitingPayment");
        order.Subtotal.ShouldBe(2000m);
        order.Total.ShouldBe(2049m);
        order.PaymentId.ShouldNotBeNull();
        order.GatewayOrderId.ShouldNotBeNullOrWhiteSpace();

        var catalogClient = fixture.CreateClientWith(CatalogPermissions.ProductsRead);
        var afterCheckout = await catalogClient.GetFromJsonAsync<ProductDto>(
            $"/api/catalog/products/{product.Id}");

        afterCheckout!.ReservedQuantity.ShouldBe(2);
        afterCheckout.OnHandQuantity.ShouldBe(10);
    }

    [DatabaseFact]
    public async Task Retrying_checkout_with_the_same_idempotency_key_replays_the_first_order()
    {
        var product = await PublishProductAsync(stock: 10, price: 500m);
        var client = fixture.CreateClientWith(OrderingPermissions.OrdersWrite);
        var key = Guid.NewGuid().ToString();

        var first = await (await Checkout(client, product.Id, 1, key)).Content.ReadFromJsonAsync<OrderDto>();
        var second = await (await Checkout(client, product.Id, 1, key)).Content.ReadFromJsonAsync<OrderDto>();

        second!.Id.ShouldBe(first!.Id);
        second.OrderNumber.ShouldBe(first.OrderNumber);

        var catalogClient = fixture.CreateClientWith(CatalogPermissions.ProductsRead);
        var afterRetry = await catalogClient.GetFromJsonAsync<ProductDto>(
            $"/api/catalog/products/{product.Id}");

        // The retry must not have reserved a second unit.
        afterRetry!.ReservedQuantity.ShouldBe(1);
    }

    [DatabaseFact]
    public async Task Reusing_an_idempotency_key_for_a_different_cart_is_a_conflict()
    {
        var product = await PublishProductAsync(stock: 10, price: 500m);
        var client = fixture.CreateClientWith(OrderingPermissions.OrdersWrite);
        var key = Guid.NewGuid().ToString();

        await Checkout(client, product.Id, 1, key);

        var different = await Checkout(client, product.Id, 3, key);

        different.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task Checkout_fails_when_the_cart_asks_for_more_than_is_available()
    {
        var product = await PublishProductAsync(stock: 2, price: 500m);
        var client = fixture.CreateClientWith(OrderingPermissions.OrdersWrite);

        var response = await Checkout(client, product.Id, quantity: 5, key: Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("catalog.stock.insufficient");
    }

    [DatabaseFact]
    public async Task An_invalid_postcode_is_rejected_before_stock_is_touched()
    {
        var product = await PublishProductAsync(stock: 5, price: 500m);
        var client = fixture.CreateClientWith(OrderingPermissions.OrdersWrite);

        var response = await client.PostAsJsonAsync(
            "/api/ordering/orders/checkout",
            new
            {
                customerId = _customerId,
                sellerId = _sellerId,
                deliveryPostcode = "22",
                lines = new[] { new { productId = product.Id, quantity = 1 } },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [DatabaseFact]
    public async Task Cancelling_an_unpaid_order_releases_the_stock_it_was_holding()
    {
        var product = await PublishProductAsync(stock: 10, price: 500m);
        var client = fixture.CreateClientWith(
            OrderingPermissions.OrdersWrite,
            OrderingPermissions.OrdersCancel);

        var order = await (await Checkout(client, product.Id, 3, Guid.NewGuid().ToString()))
            .Content.ReadFromJsonAsync<OrderDto>();

        var cancel = await client.PostAsJsonAsync(
            $"/api/ordering/orders/{order!.Id}/cancel",
            new { reason = "Customer changed their mind" });

        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var catalogClient = fixture.CreateClientWith(CatalogPermissions.ProductsRead);
        var afterCancel = await catalogClient.GetFromJsonAsync<ProductDto>(
            $"/api/catalog/products/{product.Id}");

        afterCancel!.ReservedQuantity.ShouldBe(0);
    }

    [DatabaseFact]
    public async Task The_outbox_hands_a_placed_order_to_shipping()
    {
        var product = await PublishProductAsync(stock: 10, price: 500m);
        var client = fixture.CreateClientWith(OrderingPermissions.OrdersWrite);

        var order = await (await Checkout(client, product.Id, 1, Guid.NewGuid().ToString()))
            .Content.ReadFromJsonAsync<OrderDto>();

        await fixture.DrainOutboxAsync();

        var shippingClient = fixture.CreateClientWith(ShippingPermissions.ShipmentsRead);
        var shipment = await shippingClient.GetFromJsonAsync<ShipmentDto>(
            $"/api/shipping/shipments/by-order/{order!.Id}");

        shipment.ShouldNotBeNull();
        shipment.OrderNumber.ShouldBe(order.OrderNumber);
        shipment.Status.ShouldBe("Booked");
        shipment.AwbNumber.ShouldNotBeNullOrWhiteSpace();
    }

    [DatabaseFact]
    public async Task A_captured_webhook_moves_the_order_to_paid_through_the_outbox()
    {
        var product = await PublishProductAsync(stock: 10, price: 750m);
        var client = fixture.CreateClientWith(
            OrderingPermissions.OrdersWrite,
            OrderingPermissions.OrdersRead);

        var order = await (await Checkout(client, product.Id, 1, Guid.NewGuid().ToString()))
            .Content.ReadFromJsonAsync<OrderDto>();

        var capture = await SendWebhookAsync(
            "payment.captured",
            order!.GatewayOrderId!,
            GatewayWebhook.NewGatewayPaymentId(),
            eventId: Guid.NewGuid().ToString());

        capture.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await fixture.DrainOutboxAsync();

        var afterCapture = await client.GetFromJsonAsync<OrderDto>($"/api/ordering/orders/{order.Id}");
        afterCapture!.Status.ShouldBe("Paid");

        var paymentsClient = fixture.CreateClientWith(PaymentsPermissions.PaymentsRead);
        var payment = await paymentsClient.GetFromJsonAsync<PaymentDto>(
            $"/api/payments/{order.PaymentId}");

        payment!.Status.ShouldBe("Captured");
        payment.CapturedAtUtc.ShouldNotBeNull();
    }

    private Task<HttpResponseMessage> SendWebhookAsync(
        string eventType,
        string gatewayOrderId,
        string gatewayPaymentId,
        string eventId) =>
        fixture.CreateClient().SendAsync(
            GatewayWebhook.Build(eventType, gatewayOrderId, gatewayPaymentId, eventId));

    private Task<HttpResponseMessage> Checkout(HttpClient client, Guid productId, int quantity, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ordering/orders/checkout")
        {
            Content = JsonContent.Create(new
            {
                customerId = _customerId,
                sellerId = _sellerId,
                deliveryPostcode = "221001",
                lines = new[] { new { productId, quantity } },
            }),
        };

        request.Headers.Add("Idempotency-Key", key);

        return client.SendAsync(request);
    }

    private async Task<ProductDto> PublishProductAsync(int stock, decimal price)
    {
        var categoryId = await fixture.CreateCategoryAsync("Sarees");
        var client = fixture.CreateClientWith(
            CatalogPermissions.ProductsWrite,
            CatalogPermissions.ProductsRead);

        var created = await (await client.PostAsJsonAsync(
                "/api/catalog/products",
                new
                {
                    sellerId = Guid.NewGuid(),
                    sku = $"UPB-{Guid.NewGuid():N}"[..20],
                    name = "Banarasi Silk Saree",
                    description = "Handwoven silk saree from Varanasi.",
                    categoryId,
                    price,
                    currency = "INR",
                    initialStock = stock,
                }))
            .Content.ReadFromJsonAsync<ProductDto>();

        await client.PostAsync($"/api/catalog/products/{created!.Id}/publish", null);

        return created;
    }
}
