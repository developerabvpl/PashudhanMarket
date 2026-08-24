using System.Net;
using System.Net.Http.Json;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Contracts.Permissions;
using UPBazaar.Modules.Ordering.Contracts.Dtos;
using UPBazaar.Modules.Ordering.Contracts.Permissions;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Contracts.Permissions;

namespace UPBazaar.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PaymentWebhookTests(ApiFixture fixture)
{
    [DatabaseFact]
    public async Task A_webhook_with_a_bad_signature_is_refused()
    {
        var order = await PlaceOrderAsync(price: 500m);

        var response = await SendWebhookAsync(
            "payment.captured",
            order.GatewayOrderId!,
            "pay_bad_signature",
            Guid.NewGuid().ToString(),
            signatureOverride: "not-the-right-signature");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var payment = await GetPaymentAsync(order.PaymentId!.Value);
        payment.Status.ShouldBe("Created");
    }

    [DatabaseFact]
    public async Task A_redelivered_webhook_captures_only_once()
    {
        var order = await PlaceOrderAsync(price: 1000m);
        var eventId = Guid.NewGuid().ToString();
        var gatewayPaymentId = GatewayWebhook.NewGatewayPaymentId();

        var first = await SendWebhookAsync(
            "payment.captured", order.GatewayOrderId!, gatewayPaymentId, eventId);
        var redelivery = await SendWebhookAsync(
            "payment.captured", order.GatewayOrderId!, gatewayPaymentId, eventId);

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        redelivery.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var payment = await GetPaymentAsync(order.PaymentId!.Value);
        payment.Status.ShouldBe("Captured");

        // One capture means one PaymentCaptured event on the outbox, not two.
        (await fixture.CountOutboxMessagesAsync(
            "PaymentCapturedDomainEvent",
            order.PaymentId.Value.ToString())).ShouldBe(1);
    }

    [DatabaseFact]
    public async Task A_failed_webhook_marks_the_payment_failed()
    {
        var order = await PlaceOrderAsync(price: 500m);

        var response = await SendWebhookAsync(
            "payment.failed",
            order.GatewayOrderId!,
            GatewayWebhook.NewGatewayPaymentId(),
            Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var payment = await GetPaymentAsync(order.PaymentId!.Value);
        payment.Status.ShouldBe("Failed");
    }

    [DatabaseFact]
    public async Task A_refund_is_applied_once_however_often_it_is_retried()
    {
        var order = await PlaceOrderAsync(price: 1000m);
        await SendWebhookAsync(
            "payment.captured",
            order.GatewayOrderId!,
            GatewayWebhook.NewGatewayPaymentId(),
            Guid.NewGuid().ToString());

        var client = fixture.CreateClientWith(
            PaymentsPermissions.RefundsWrite,
            PaymentsPermissions.PaymentsRead);
        var key = Guid.NewGuid().ToString();

        var first = await RefundAsync(client, order.PaymentId!.Value, 400m, key);
        var retry = await RefundAsync(client, order.PaymentId.Value, 400m, key);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);

        var firstRefund = await first.Content.ReadFromJsonAsync<RefundDto>();
        var retriedRefund = await retry.Content.ReadFromJsonAsync<RefundDto>();
        retriedRefund!.Id.ShouldBe(firstRefund!.Id);

        var payment = await GetPaymentAsync(order.PaymentId.Value);
        payment.Status.ShouldBe("PartiallyRefunded");
    }

    [DatabaseFact]
    public async Task A_refund_larger_than_the_captured_amount_is_refused()
    {
        var order = await PlaceOrderAsync(price: 1000m);
        await SendWebhookAsync(
            "payment.captured",
            order.GatewayOrderId!,
            GatewayWebhook.NewGatewayPaymentId(),
            Guid.NewGuid().ToString());

        var client = fixture.CreateClientWith(PaymentsPermissions.RefundsWrite);

        var response = await RefundAsync(
            client,
            order.PaymentId!.Value,
            amount: 99_999m,
            key: Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task Refunding_needs_the_refund_permission()
    {
        var order = await PlaceOrderAsync(price: 500m);
        var client = fixture.CreateClientWith(PaymentsPermissions.PaymentsRead);

        var response = await RefundAsync(
            client,
            order.PaymentId!.Value,
            100m,
            Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static Task<HttpResponseMessage> RefundAsync(
        HttpClient client,
        Guid paymentId,
        decimal amount,
        string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/{paymentId}/refunds")
        {
            Content = JsonContent.Create(new { amount, reason = "Damaged in transit" }),
        };

        request.Headers.Add("Idempotency-Key", key);

        return client.SendAsync(request);
    }

    private async Task<PaymentDto> GetPaymentAsync(Guid paymentId)
    {
        var client = fixture.CreateClientWith(PaymentsPermissions.PaymentsRead);
        var payment = await client.GetFromJsonAsync<PaymentDto>($"/api/payments/{paymentId}");

        return payment.ShouldNotBeNull();
    }

    private Task<HttpResponseMessage> SendWebhookAsync(
        string eventType,
        string gatewayOrderId,
        string gatewayPaymentId,
        string eventId,
        string? signatureOverride = null) =>
        fixture.CreateClient().SendAsync(
            GatewayWebhook.Build(eventType, gatewayOrderId, gatewayPaymentId, eventId, signatureOverride));

    private async Task<OrderDto> PlaceOrderAsync(decimal price)
    {
        var categoryId = await fixture.CreateCategoryAsync("Sarees");
        var catalogClient = fixture.CreateClientWith(
            CatalogPermissions.ProductsWrite,
            CatalogPermissions.ProductsRead);

        var product = await (await catalogClient.PostAsJsonAsync(
                "/api/catalog/products",
                new
                {
                    sellerId = Guid.NewGuid(),
                    sku = $"UPB-{Guid.NewGuid():N}"[..20],
                    name = "Banarasi Silk Saree",
                    categoryId,
                    price,
                    currency = "INR",
                    initialStock = 10,
                }))
            .ReadAsync<ProductDto>(System.Net.HttpStatusCode.Created);

        await catalogClient.PostAsync($"/api/catalog/products/{product!.Id}/publish", null);

        var orderingClient = fixture.CreateClientWith(OrderingPermissions.OrdersWrite);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ordering/orders/checkout")
        {
            Content = JsonContent.Create(new
            {
                customerId = Guid.NewGuid(),
                sellerId = Guid.NewGuid(),
                deliveryPostcode = "221001",
                lines = new[] { new { productId = product.Id, quantity = 1 } },
            }),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var order = await (await orderingClient.SendAsync(request)).Content.ReadFromJsonAsync<OrderDto>();

        return order.ShouldNotBeNull();
    }
}
