using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Outbox;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Infrastructure.Persistence.Migrations;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.Modules.Orders.Services;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.Modules.Payments.Gateway;
using UPBazaar.Modules.Payments.Services;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Payments;

/// <summary>
/// The whole online payment path against the fake gateway, which signs exactly as Razorpay does
/// with a known secret - so these tests forge nothing a real attacker could not also forge with the
/// real secret, and prove the checks refuse everything else.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PaymentTests(ApiFixture fixture)
{
    private static readonly Uri VerifyUri = new("/api/v1/payments/razorpay/verify", UriKind.Relative);
    private static readonly Uri WebhookUri = new("/api/v1/payments/webhooks/razorpay", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task Online_payment_is_offered_through_the_fake_gateway_in_tests()
    {
        var config = await fixture.CreateClient().GetFromJsonAsync<PaymentsConfigDto>(
            new Uri("/api/v1/payments/config", UriKind.Relative));

        config.ShouldBe(new PaymentsConfigDto(true, FakeGateway.GatewayName));
    }

    [DatabaseFact]
    public async Task A_verified_payment_confirms_the_order_and_commits_its_stock()
    {
        var (admin, buyer, product, order) = await UnpaidOrderAsync(price: 250m, quantity: 2);

        var session = await StartAsync(buyer, order.Id);
        session.AmountInPaise.ShouldBe(50_000);
        session.Currency.ShouldBe("INR");
        session.OrderNumber.ShouldBe(order.Number);
        session.ExpiresAtUtc.ShouldBe(order.PaymentDueAtUtc!.Value, TimeSpan.FromSeconds(1));

        var result = await VerifyAsync(buyer, session.GatewayOrderId, "pay_test_1");

        result.ShouldBe(new PaymentResultDto(order.Id, "Confirmed"));
        (await OrderAsync(buyer, order.Id)).Status.ShouldBe("Confirmed");
        (await StockAsync(admin, product.Id)).ShouldBe(new StockLevelDto(product.Id, 3, 0, 3));
    }

    [DatabaseFact]
    public async Task Starting_twice_reuses_the_same_gateway_order()
    {
        var (_, buyer, _, order) = await UnpaidOrderAsync(price: 100m, quantity: 1);

        var first = await StartAsync(buyer, order.Id);
        var second = await StartAsync(buyer, order.Id);

        second.GatewayOrderId.ShouldBe(first.GatewayOrderId);
        second.PaymentId.ShouldBe(first.PaymentId);
    }

    [DatabaseFact]
    public async Task A_forged_signature_is_refused_and_nothing_is_paid()
    {
        var (_, buyer, _, order) = await UnpaidOrderAsync(price: 100m, quantity: 1);
        var session = await StartAsync(buyer, order.Id);

        var response = await buyer.PostAsJsonAsync(VerifyUri, new
        {
            gatewayOrderId = session.GatewayOrderId,
            gatewayPaymentId = "pay_forged",
            signature = RazorpaySignature.ForPayment(session.GatewayOrderId, "pay_forged", "not-the-secret"),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await OrderAsync(buyer, order.Id)).Status.ShouldBe("PendingPayment");
    }

    [DatabaseFact]
    public async Task Another_buyer_cannot_pay_or_verify_someone_elses_order()
    {
        var (_, buyer, _, order) = await UnpaidOrderAsync(price: 100m, quantity: 1);
        var other = await BuyerClientAsync();
        var session = await StartAsync(buyer, order.Id);

        (await other.PostAsync(StartUri(order.Id), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var response = await other.PostAsJsonAsync(VerifyUri, SignedVerifyBody(session.GatewayOrderId, "pay_other"));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task The_webhook_confirms_a_payment_the_browser_never_reported_and_ignores_the_duplicate()
    {
        var (_, buyer, _, order) = await UnpaidOrderAsync(price: 120m, quantity: 1);
        var session = await StartAsync(buyer, order.Id);
        var eventId = $"evt_{Guid.NewGuid():N}";
        var body = CapturedEvent(session.GatewayOrderId, "pay_webhook_1", session.AmountInPaise);

        (await PostWebhookAsync(body, RazorpaySignature.ForWebhook(body, FakeGateway.WebhookSecret), eventId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await OrderAsync(buyer, order.Id)).Status.ShouldBe("Confirmed");

        (await PostWebhookAsync(body, RazorpaySignature.ForWebhook(body, FakeGateway.WebhookSecret), eventId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // And the browser reporting the same payment afterwards changes nothing.
        (await VerifyAsync(buyer, session.GatewayOrderId, "pay_webhook_1")).Outcome.ShouldBe("Confirmed");
    }

    [DatabaseFact]
    public async Task An_unsigned_or_wrongly_signed_webhook_is_rejected()
    {
        var (_, buyer, _, order) = await UnpaidOrderAsync(price: 120m, quantity: 1);
        var session = await StartAsync(buyer, order.Id);
        var body = CapturedEvent(session.GatewayOrderId, "pay_forged", session.AmountInPaise);

        (await PostWebhookAsync(body, signature: null, eventId: null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostWebhookAsync(body, RazorpaySignature.ForWebhook(body, "guess"), eventId: null))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await OrderAsync(buyer, order.Id)).Status.ShouldBe("PendingPayment");
    }

    [DatabaseFact]
    public async Task A_payment_that_lands_after_the_order_was_cancelled_is_owed_back_in_full()
    {
        var (_, buyer, _, order) = await UnpaidOrderAsync(price: 80m, quantity: 2);
        var session = await StartAsync(buyer, order.Id);

        (await buyer.PostAsJsonAsync(new Uri($"/api/v1/orders/{order.Id}/cancel", UriKind.Relative), new { }))
            .EnsureSuccessStatusCode();

        var result = await VerifyAsync(buyer, session.GatewayOrderId, "pay_late");

        result.Outcome.ShouldBe("RefundDue");

        var refund = (await RefundsForAsync(order.Id)).ShouldHaveSingleItem();
        refund.Amount.ShouldBe(160m);
        refund.Status.ShouldBe("Due");
        refund.GatewayPaymentId.ShouldBe("pay_late");
        refund.ReasonCode.ShouldBe("PaymentRefused");
        refund.Reason.ShouldStartWith("Payment could not be applied to the order: ");
    }

    [DatabaseFact]
    public async Task An_unpaid_payment_whose_order_is_cancelled_is_abandoned_and_late_money_is_still_owed_back()
    {
        var (admin, buyer, _, order) = await UnpaidOrderAsync(price: 90m, quantity: 2);
        var session = await StartAsync(buyer, order.Id);

        (await buyer.PostAsJsonAsync(new Uri($"/api/v1/orders/{order.Id}/cancel", UriKind.Relative), new { }))
            .EnsureSuccessStatusCode();

        await ProcessOutboxAsync();

        // No longer "awaiting the buyer": nobody can pay for a cancelled order.
        var abandoned = await PaymentForAsync(admin, order.Number);
        abandoned.Status.ShouldBe("Abandoned");
        abandoned.OrderOutcome.ShouldBe("Pending");
        abandoned.RefundDue.ShouldBe(0m);

        var filtered = await admin.GetFromJsonAsync<PagedList<PaymentDto>>(
            new Uri($"/api/v1/admin/payments?status=Abandoned&search={order.Number}", UriKind.Relative));
        filtered!.Items.ShouldHaveSingleItem().Id.ShouldBe(abandoned.Id);

        // The bank can still capture after that. The money is recorded and all of it is owed back.
        (await VerifyAsync(buyer, session.GatewayOrderId, "pay_after_abandon")).Outcome.ShouldBe("RefundDue");

        var late = await PaymentForAsync(admin, order.Number);
        late.Status.ShouldBe("Paid");
        late.OrderOutcome.ShouldBe("Refused");
        late.RefundDue.ShouldBe(180m);
    }

    /// <summary>
    /// The AbandonedPayments migration catches up payments left awaiting a buyer by orders that were
    /// cancelled before a cancellation abandoned them. The fixture's database was empty when it ran,
    /// so the backfill is run again here over a payment put back the old way - still Created.
    /// </summary>
    [DatabaseFact]
    public async Task The_backfill_abandons_unpaid_payments_of_cancelled_orders_and_leaves_open_ones_alone()
    {
        var (admin, buyer, _, cancelled) = await UnpaidOrderAsync(price: 70m, quantity: 1);
        await StartAsync(buyer, cancelled.Id);

        (await buyer.PostAsJsonAsync(new Uri($"/api/v1/orders/{cancelled.Id}/cancel", UriKind.Relative), new { }))
            .EnsureSuccessStatusCode();

        await ProcessOutboxAsync();

        var (_, otherBuyer, _, open) = await UnpaidOrderAsync(price: 70m, quantity: 1);
        await StartAsync(otherBuyer, open.Id);

        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE payments.Payments SET Status = 'Created' WHERE OrderId = {cancelled.Id}");
            await dbContext.Database.ExecuteSqlRawAsync(AbandonedPayments.BackfillSql);
        }

        (await PaymentForAsync(admin, cancelled.Number)).Status.ShouldBe("Abandoned");
        (await PaymentForAsync(admin, open.Number)).Status.ShouldBe("Created");
    }

    [DatabaseFact]
    public async Task Cancelling_part_of_a_paid_order_records_that_parts_refund()
    {
        var admin = await AdminClientAsync();
        var kept = await CreateProductAsync(admin, price: 100m, stock: 5, Guid.NewGuid());
        var dropped = await CreateProductAsync(admin, price: 40m, stock: 5, Guid.NewGuid());
        var buyer = await BuyerClientAsync();

        await AddToCartAsync(buyer, kept.Id, 1);
        await AddToCartAsync(buyer, dropped.Id, 2);
        var order = await PlaceOnlineAsync(buyer);
        var session = await StartAsync(buyer, order.Id);
        await VerifyAsync(buyer, session.GatewayOrderId, "pay_parts");

        var part = order.Parts.Single(p => p.Lines.Any(l => l.ProductId == dropped.Id));
        (await admin.PostAsJsonAsync(
                new Uri($"/api/v1/admin/orders/{order.Id}/parts/{part.Id}/cancel", UriKind.Relative),
                new { reason = "Seller cannot supply" }))
            .EnsureSuccessStatusCode();

        await ProcessOutboxAsync();

        var refund = (await RefundsForAsync(order.Id)).ShouldHaveSingleItem();
        refund.Amount.ShouldBe(80m);
        refund.OrderPartId.ShouldBe(part.Id);
        refund.Reason.ShouldBe("Part of the order was cancelled.");
        refund.ReasonCode.ShouldBe("PartCancelled");

        // The list works the refund out from its own total; it must agree with the refund recorded.
        var listed = (await buyer.GetFromJsonAsync<PagedList<OrderSummaryDto>>(
            new Uri("/api/v1/orders", UriKind.Relative)))!.Items.Single(o => o.Id == order.Id);
        listed.RefundTotal.ShouldBe(80m);
        (listed.AmountPaid - listed.Total).ShouldBe(80m);

        var marked = await (await admin.PostAsJsonAsync(
                new Uri($"/api/v1/admin/payments/refunds/{refund.Id}/mark-refunded", UriKind.Relative),
                new { gatewayRefundId = "rfnd_test_1" }))
            .Content.ReadFromJsonAsync<RefundDto>();

        marked!.Status.ShouldBe("Refunded");
        marked.GatewayRefundId.ShouldBe("rfnd_test_1");
        marked.RefundedBy.ShouldNotBeNull();
    }

    [DatabaseFact]
    public async Task Cancelling_a_paid_order_shows_what_was_paid_and_refunded_and_marks_the_payment_cancelled()
    {
        var (admin, buyer, _, order) = await UnpaidOrderAsync(price: 119m, quantity: 4);
        var session = await StartAsync(buyer, order.Id);
        await VerifyAsync(buyer, session.GatewayOrderId, "pay_whole");

        (await buyer.PostAsJsonAsync(new Uri($"/api/v1/orders/{order.Id}/cancel", UriKind.Relative), new { }))
            .EnsureSuccessStatusCode();

        await ProcessOutboxAsync();

        // The total counts only what the buyer keeps; the money is told apart from it.
        var cancelled = await OrderAsync(buyer, order.Id);
        cancelled.Status.ShouldBe("Cancelled");
        cancelled.Total.ShouldBe(0m);
        cancelled.AmountPaid.ShouldBe(476m);
        cancelled.RefundTotal.ShouldBe(476m);

        // The buyer's order list says the same, rather than a bare zero.
        var listed = (await buyer.GetFromJsonAsync<PagedList<OrderSummaryDto>>(
            new Uri("/api/v1/orders", UriKind.Relative)))!.Items.Single(o => o.Id == order.Id);
        listed.Total.ShouldBe(0m);
        listed.AmountPaid.ShouldBe(476m);
        listed.RefundTotal.ShouldBe(476m);

        var refund = (await RefundsForAsync(order.Id)).ShouldHaveSingleItem();
        refund.Amount.ShouldBe(476m);
        refund.Reason.ShouldBe("The order was cancelled.");
        refund.ReasonCode.ShouldBe("OrderCancelled");

        var payment = (await admin.GetFromJsonAsync<PagedList<PaymentDto>>(
            new Uri($"/api/v1/admin/payments?search={order.Number}", UriKind.Relative)))!.Items.ShouldHaveSingleItem();
        payment.OrderOutcome.ShouldBe("Cancelled");
        payment.RefundDue.ShouldBe(476m);
    }

    [DatabaseFact]
    public async Task Cancelling_the_last_part_left_is_coded_as_the_order_cancelled_and_an_earlier_one_as_a_part()
    {
        var admin = await AdminClientAsync();
        var first = await CreateProductAsync(admin, price: 100m, stock: 5, Guid.NewGuid());
        var second = await CreateProductAsync(admin, price: 40m, stock: 5, Guid.NewGuid());
        var buyer = await BuyerClientAsync();

        await AddToCartAsync(buyer, first.Id, 1);
        await AddToCartAsync(buyer, second.Id, 1);
        var order = await PlaceOnlineAsync(buyer);
        var session = await StartAsync(buyer, order.Id);
        await VerifyAsync(buyer, session.GatewayOrderId, "pay_two_parts");

        var firstPart = order.Parts.Single(p => p.Lines.Any(l => l.ProductId == first.Id));
        var secondPart = order.Parts.Single(p => p.Lines.Any(l => l.ProductId == second.Id));

        foreach (var part in new[] { firstPart, secondPart })
        {
            (await admin.PostAsJsonAsync(
                    new Uri($"/api/v1/admin/orders/{order.Id}/parts/{part.Id}/cancel", UriKind.Relative),
                    new { reason = "Seller cannot supply" }))
                .EnsureSuccessStatusCode();
        }

        await ProcessOutboxAsync();

        (await OrderAsync(buyer, order.Id)).Status.ShouldBe("Cancelled");

        var refunds = await RefundsForAsync(order.Id);
        refunds.Count.ShouldBe(2);

        var earlier = refunds.Single(r => r.OrderPartId == firstPart.Id);
        earlier.ReasonCode.ShouldBe("PartCancelled");
        earlier.Reason.ShouldBe("Part of the order was cancelled.");

        var last = refunds.Single(r => r.OrderPartId == secondPart.Id);
        last.ReasonCode.ShouldBe("OrderCancelled");
        last.Reason.ShouldBe("The order was cancelled.");
    }

    [DatabaseFact]
    public async Task The_settlement_job_finishes_a_payment_that_was_captured_but_never_applied()
    {
        var (_, buyer, _, order) = await UnpaidOrderAsync(price: 90m, quantity: 1);
        var session = await StartAsync(buyer, order.Id);

        // The state a crash between recording the money and confirming the order leaves behind.
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();
            var payment = await dbContext.Set<Payment>().SingleAsync(p => p.GatewayOrderId == session.GatewayOrderId);

            payment.MarkPaid("pay_stranded", DateTime.UtcNow.AddMinutes(-5));
            await dbContext.SaveChangesAsync();
        }

        (await OrderAsync(buyer, order.Id)).Status.ShouldBe("PendingPayment");

        using (var scope = fixture.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<PaymentSettlementJob>().SettleAsync(default))
                .ShouldBeGreaterThanOrEqualTo(1);
        }

        (await OrderAsync(buyer, order.Id)).Status.ShouldBe("Confirmed");
    }

    [DatabaseFact]
    public async Task Buyers_cannot_see_payments_and_a_paid_order_cannot_be_paid_again()
    {
        var (_, buyer, _, order) = await UnpaidOrderAsync(price: 60m, quantity: 1);
        var session = await StartAsync(buyer, order.Id);
        await VerifyAsync(buyer, session.GatewayOrderId, "pay_once");

        (await buyer.PostAsync(StartUri(order.Id), null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await buyer.GetAsync(new Uri("/api/v1/admin/payments", UriKind.Relative))).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var admin = await AdminClientAsync();
        var found = await admin.GetFromJsonAsync<PagedList<PaymentDto>>(
            new Uri($"/api/v1/admin/payments?search={order.Number}", UriKind.Relative));

        var payment = found!.Items.ShouldHaveSingleItem();
        payment.Status.ShouldBe("Paid");
        payment.OrderOutcome.ShouldBe("Confirmed");
        payment.GatewayPaymentId.ShouldBe("pay_once");
    }

    private async Task<(HttpClient Admin, HttpClient Buyer, ProductDto Product, OrderDto Order)> UnpaidOrderAsync(
        decimal price,
        int quantity)
    {
        var admin = await AdminClientAsync();
        var product = await CreateProductAsync(admin, price, stock: 5, Guid.NewGuid());
        var buyer = await BuyerClientAsync();

        await AddToCartAsync(buyer, product.Id, quantity);

        return (admin, buyer, product, await PlaceOnlineAsync(buyer));
    }

    private async Task<HttpClient> AdminClientAsync() =>
        fixture.CreateAuthenticatedClient((await _auth.SignInAsSuperAdminAsync()).AccessToken);

    private async Task<HttpClient> BuyerClientAsync() =>
        fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile())).AccessToken);

    private async Task<HttpResponseMessage> PostWebhookAsync(string body, string? signature, string? eventId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (signature is not null)
        {
            request.Headers.Add("X-Razorpay-Signature", signature);
        }

        if (eventId is not null)
        {
            request.Headers.Add("X-Razorpay-Event-Id", eventId);
        }

        return await fixture.CreateClient().SendAsync(request);
    }

    private async Task<IReadOnlyList<RefundDto>> RefundsForAsync(Guid orderId)
    {
        var admin = await AdminClientAsync();
        var refunds = await admin.GetFromJsonAsync<PagedList<RefundDto>>(
            new Uri("/api/v1/admin/payments/refunds?pageSize=100", UriKind.Relative));

        return [.. refunds!.Items.Where(r => r.OrderId == orderId)];
    }

    /// <summary>
    /// Drains the outbox. The whole suite shares one database, so events from every other test
    /// are queued ahead of this one's, oldest first; one batch is not enough to reach it.
    /// </summary>
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

    private static async Task<PaymentDto> PaymentForAsync(HttpClient admin, string orderNumber) =>
        (await admin.GetFromJsonAsync<PagedList<PaymentDto>>(
            new Uri($"/api/v1/admin/payments?search={orderNumber}", UriKind.Relative)))!.Items.ShouldHaveSingleItem();

    private static async Task<CheckoutSessionDto> StartAsync(HttpClient buyer, Guid orderId)
    {
        var response = await buyer.PostAsync(StartUri(orderId), null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CheckoutSessionDto>())!;
    }

    private static async Task<PaymentResultDto> VerifyAsync(HttpClient buyer, string gatewayOrderId, string paymentId)
    {
        var response = await buyer.PostAsJsonAsync(VerifyUri, SignedVerifyBody(gatewayOrderId, paymentId));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<PaymentResultDto>())!;
    }

    private static object SignedVerifyBody(string gatewayOrderId, string paymentId) => new
    {
        gatewayOrderId,
        gatewayPaymentId = paymentId,
        signature = RazorpaySignature.ForPayment(gatewayOrderId, paymentId, FakeGateway.KeySecret),
    };

    /// <summary>The shape Razorpay posts for <c>payment.captured</c>, cut down to the fields read.</summary>
    private static string CapturedEvent(string gatewayOrderId, string paymentId, long amountInPaise) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            entity = "event",
            @event = "payment.captured",
            payload = new
            {
                payment = new
                {
                    entity = new { id = paymentId, order_id = gatewayOrderId, amount = amountInPaise, status = "captured" },
                },
            },
        });

    private static Uri StartUri(Guid orderId) => new($"/api/v1/payments/orders/{orderId}/checkout", UriKind.Relative);

    private static async Task<OrderDto> PlaceOnlineAsync(HttpClient buyer)
    {
        var response = await buyer.PostAsJsonAsync(new Uri("/api/v1/orders", UriKind.Relative), new
        {
            paymentMethod = "Online",
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

    private static async Task AddToCartAsync(HttpClient buyer, Guid productId, int quantity) =>
        (await buyer.PutAsJsonAsync(new Uri($"/api/v1/cart/items/{productId}", UriKind.Relative), new { quantity }))
        .EnsureSuccessStatusCode();

    private static async Task<StockLevelDto> StockAsync(HttpClient admin, Guid productId) =>
        (await admin.GetFromJsonAsync<StockDetailDto>(
            new Uri($"/api/v1/admin/inventory/stock/{productId}", UriKind.Relative)))!.Level;

    private static async Task<ProductDto> CreateProductAsync(HttpClient admin, decimal price, int stock, Guid sellerId)
    {
        var category = await (await admin.PostAsJsonAsync(
                new Uri("/api/v1/admin/catalog/categories", UriKind.Relative),
                new { name = $"Category {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<CategoryDto>();

        var response = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/catalog/products", UriKind.Relative), new
        {
            sku = $"PAY-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            name = "Panchgavya Sabun, 100g",
            price,
            sellerId,
            categoryId = category!.Id,
            onHandQuantity = stock,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var product = (await response.Content.ReadFromJsonAsync<ProductDto>())!;

        (await admin.PostAsync(new Uri($"/api/v1/admin/catalog/products/{product.Id}/publish", UriKind.Relative), null))
            .EnsureSuccessStatusCode();

        return product;
    }
}
