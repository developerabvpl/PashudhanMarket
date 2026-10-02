using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Outbox;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.Modules.Notifications.Domain;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Gateway;
using UPBazaar.Modules.Sellers.Domain;
using UPBazaar.Modules.Settlements.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Shipping;

/// <summary>
/// A buyer sending a delivered parcel back: asking, the seller or staff deciding, a courier
/// collecting it, the seller inspecting it, and the refund falling due.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BuyerReturnTests(ApiFixture fixture)
{
    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task A_cash_return_approved_by_the_seller_comes_back_and_is_refunded_to_the_buyers_upi_id()
    {
        var delivered = await DeliveredAsync("CashOnDelivery", withSellerAccount: true);
        var (admin, buyer, seller, product, order) = (delivered.Admin, delivered.Buyer, delivered.Seller!, delivered.Product, delivered.Order);
        var part = order.Parts.Single();

        part.ReturnableUntilUtc.ShouldNotBeNull();
        part.DeliveredAtUtc.ShouldNotBeNull();

        // Cash was paid at the door, so there is nothing to reverse: the buyer must say where the refund goes.
        (await RequestReturnAsync(buyer, order, "Damaged", upiId: null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RequestReturnAsync(buyer, order, "Damaged", upiId: "not a upi id")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var requested = await RequestReturnAsync(buyer, order, "Damaged", upiId: "asha.devi@okicici", comment: "Two diyas arrived cracked.");
        requested.StatusCode.ShouldBe(HttpStatusCode.OK, await requested.Content.ReadAsStringAsync());
        (await requested.Content.ReadFromJsonAsync<OrderDto>())!.Parts.Single().ReturnRequest!.Status.ShouldBe("Requested");

        // The seller sees it waiting, without the buyer's UPI id.
        var queue = await seller.GetFromJsonAsync<PagedList<ReturnRequestSummaryDto>>(
            new Uri("/api/v1/seller/orders/returns?status=Requested", UriKind.Relative));
        queue!.Items.ShouldHaveSingleItem().Comment.ShouldBe("Two diyas arrived cracked.");
        var sellerView = await seller.GetFromJsonAsync<SellerOrderDto>(new Uri($"/api/v1/seller/orders/{order.Id}", UriKind.Relative));
        sellerView!.ReturnRequest!.RefundUpiId.ShouldBeNull();

        var approved = await seller.PostAsJsonAsync(DecisionUri("seller", order), new { approve = true });
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        (await approved.Content.ReadFromJsonAsync<SellerOrderDto>())!.Status.ShouldBe("Returning");

        // Approval books the courier to collect from the buyer.
        await ProcessOutboxAsync();
        var pickup = (await ShipmentsAsync(buyer, order)).Single(s => s.Direction == "Return");
        pickup.Status.ShouldBe("PickupRequested");
        pickup.PaymentMode.ShouldBe("Prepaid");
        pickup.Parcel.WeightGrams.ShouldBe(300);

        await CourierAsync(pickup.Awb!, "RETURN PICKED UP");
        (await OrderAsync(buyer, order.Id)).Parts.Single().Status.ShouldBe("Returning");

        await CourierAsync(pickup.Awb!, "RETURN DELIVERED");

        var back = await OrderAsync(buyer, order.Id);
        back.Parts.Single().Status.ShouldBe("Returned");

        // The goods did reach the buyer, so the order is completed, not cancelled.
        back.Status.ShouldBe("Completed");

        await ProcessOutboxAsync();
        var refund = (await RefundsForAsync(admin, order.Id)).ShouldHaveSingleItem();
        refund.Method.ShouldBe("Upi");
        refund.ReasonCode.ShouldBe("BuyerReturn");
        refund.UpiId.ShouldBe("asha.devi@okicici");
        refund.Amount.ShouldBe(150m);
        refund.Status.ShouldBe("Due");
        refund.PaymentId.ShouldBeNull();

        // Inspected by the seller like any parcel that comes back.
        (await StockAsync(admin, product.Id)).OnHandQuantity.ShouldBe(8);
        (await seller.PostAsJsonAsync(
                new Uri($"/api/v1/seller/orders/{order.Id}/parts/{part.Id}/return-inspection", UriKind.Relative),
                new { condition = "Good" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StockAsync(admin, product.Id)).OnHandQuantity.ShouldBe(10);

        (await admin.PostAsJsonAsync(
                new Uri($"/api/v1/admin/payments/refunds/{refund.Id}/mark-refunded", UriKind.Relative),
                new { gatewayRefundId = "412345678901" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RefundsForAsync(admin, order.Id)).Single().Status.ShouldBe("Refunded");

        // The buyer is texted where the money went.
        await ProcessOutboxAsync();

        using var scope = fixture.CreateScope();
        var texts = await scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>().Set<NotificationMessage>()
            .AsNoTracking()
            .Where(m => m.Body.Contains(order.Number))
            .Select(m => new { m.Template, m.Body })
            .ToListAsync();

        texts.ShouldContain(t => t.Template == "return-approved");
        texts.ShouldContain(t => t.Template == "refund-made" && t.Body.Contains("Rs. 150") && t.Body.Contains("asha.devi@okicici"));
    }

    [DatabaseFact]
    public async Task An_online_return_approved_by_staff_is_refunded_through_the_payment()
    {
        var delivered = await DeliveredAsync("Online", withSellerAccount: false);
        var (admin, buyer, order) = (delivered.Admin, delivered.Buyer, delivered.Order);

        // A UPI id is not needed and not kept: the money goes back the way it came.
        (await RequestReturnAsync(buyer, order, "WrongItem", upiId: "asha@okicici")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OrderAsync(buyer, order.Id)).Parts.Single().ReturnRequest!.RefundUpiId.ShouldBeNull();

        var queue = await admin.GetFromJsonAsync<PagedList<ReturnRequestSummaryDto>>(
            new Uri("/api/v1/admin/orders/returns?status=Requested&pageSize=100", UriKind.Relative));
        queue!.Items.ShouldContain(r => r.OrderId == order.Id && r.PaymentMethod == "Online");

        (await admin.PostAsJsonAsync(DecisionUri("admin", order), new { approve = true })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ProcessOutboxAsync();

        var pickup = (await ShipmentsAsync(buyer, order)).Single(s => s.Direction == "Return");
        await CourierAsync(pickup.Awb!, "DELIVERED");
        await ProcessOutboxAsync();

        var refund = (await RefundsForAsync(admin, order.Id)).ShouldHaveSingleItem();
        refund.Method.ShouldBe("Razorpay");
        refund.ReasonCode.ShouldBe("BuyerReturn");
        refund.PaymentId.ShouldNotBeNull();
        refund.GatewayPaymentId.ShouldNotBeNull();
        refund.Amount.ShouldBe(150m);
    }

    [DatabaseFact]
    public async Task A_refused_return_tells_the_buyer_why_and_cannot_be_asked_again()
    {
        var delivered = await DeliveredAsync("CashOnDelivery", withSellerAccount: true);
        var (buyer, seller, order) = (delivered.Buyer, delivered.Seller!, delivered.Order);

        await RequestReturnAsync(buyer, order, "NoLongerNeeded", upiId: "asha@okicici");

        // Refusing needs a reason the buyer can read.
        (await seller.PostAsJsonAsync(DecisionUri("seller", order), new { approve = false })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await seller.PostAsJsonAsync(DecisionUri("seller", order), new { approve = false, note = "Opened food cannot be taken back." }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var part = (await OrderAsync(buyer, order.Id)).Parts.Single();
        part.Status.ShouldBe("Delivered");
        part.ReturnRequest!.Status.ShouldBe("Rejected");
        part.ReturnRequest.DecisionNote.ShouldBe("Opened food cannot be taken back.");

        (await RequestReturnAsync(buyer, order, "Other", upiId: "asha@okicici", comment: "Please reconsider"))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ShipmentsAsync(buyer, order)).ShouldNotContain(s => s.Direction == "Return");
    }

    [DatabaseFact]
    public async Task Only_the_buyer_can_ask_and_only_once_the_parcel_is_delivered()
    {
        var shipped = await ShippedAsync("CashOnDelivery", withSellerAccount: false);

        (await RequestReturnAsync(shipped.Buyer, shipped.Order, "Damaged", upiId: "asha@okicici"))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await CourierAsync(shipped.Awb, "DELIVERED");

        var stranger = fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile())).AccessToken);
        (await RequestReturnAsync(stranger, shipped.Order, "Damaged", upiId: "asha@okicici"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task Returning_one_of_two_refunds_that_one_and_the_seller_is_paid_for_the_other()
    {
        var delivered = await DeliveredAsync("Online", withSellerAccount: true);
        var (admin, buyer, seller, product, order) = (delivered.Admin, delivered.Buyer, delivered.Seller!, delivered.Product, delivered.Order);
        var part = order.Parts.Single();

        (await buyer.PostAsJsonAsync(
                new Uri($"/api/v1/orders/{order.Id}/parts/{part.Id}/return", UriKind.Relative),
                new { reason = "Damaged", items = new[] { new { productId = product.Id, quantity = 3 } } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var requested = await buyer.PostAsJsonAsync(
            new Uri($"/api/v1/orders/{order.Id}/parts/{part.Id}/return", UriKind.Relative),
            new { reason = "Damaged", items = new[] { new { productId = product.Id, quantity = 1 } } });
        requested.StatusCode.ShouldBe(HttpStatusCode.OK, await requested.Content.ReadAsStringAsync());
        (await requested.Content.ReadFromJsonAsync<OrderDto>())!.Parts.Single().Lines.Single().ReturnQuantity.ShouldBe(1);

        (await admin.PostAsJsonAsync(DecisionUri("admin", order), new { approve = true })).EnsureSuccessStatusCode();
        await ProcessOutboxAsync();

        var returning = await OrderAsync(buyer, order.Id);
        returning.Subtotal.ShouldBe(75m);
        returning.Parts.Single().ReturnRequest!.RefundDue.ShouldBe(75m);

        var pickup = (await ShipmentsAsync(buyer, order)).Single(s => s.Direction == "Return");
        await CourierAsync(pickup.Awb!, "RETURN PICKED UP");
        await CourierAsync(pickup.Awb!, "RETURN DELIVERED");
        await ProcessOutboxAsync();

        (await RefundsForAsync(admin, order.Id)).ShouldHaveSingleItem().Amount.ShouldBe(75m);

        var earnings = await admin.GetFromJsonAsync<PagedList<EarningDto>>(
            new Uri($"/api/v1/admin/settlements/earnings?sellerId={returning.Parts.Single().SellerId}", UriKind.Relative));
        var sale = earnings!.Items.Single(e => e.Kind == "Sale");
        sale.GrossAmount.ShouldBe(75m);
        sale.Status.ShouldBe("Accruing");

        // Only the unit that came back is inspected and restocked.
        (await StockAsync(admin, product.Id)).OnHandQuantity.ShouldBe(8);
        (await seller.PostAsJsonAsync(
                new Uri($"/api/v1/seller/orders/{order.Id}/parts/{part.Id}/return-inspection", UriKind.Relative),
                new { lines = new[] { new { productId = product.Id, condition = "Good" } } }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StockAsync(admin, product.Id)).OnHandQuantity.ShouldBe(9);
    }

    private sealed record Setup(HttpClient Admin, HttpClient Buyer, HttpClient? Seller, ProductDto Product, OrderDto Order, string Awb);

    private async Task<Setup> DeliveredAsync(string paymentMethod, bool withSellerAccount)
    {
        var shipped = await ShippedAsync(paymentMethod, withSellerAccount);

        await CourierAsync(shipped.Awb, "DELIVERED");

        return shipped with { Order = await OrderAsync(shipped.Buyer, shipped.Order.Id) };
    }

    /// <summary>
    /// Two of a 75-rupee product, from a registered seller - a return needs the seller's address -
    /// bought, booked with the courier and picked up.
    /// </summary>
    private async Task<Setup> ShippedAsync(string paymentMethod, bool withSellerAccount)
    {
        var admin = await AdminClientAsync();
        var sellerId = await SellerAsync();
        var seller = withSellerAccount ? await SellerAccountAsync(admin, sellerId) : null;

        (await admin.PutAsJsonAsync(new Uri("/api/v1/admin/shipping/pickup-locations", UriKind.Relative), new { sellerId, name = "Buyer Return Gaushala" }))
            .EnsureSuccessStatusCode();

        var product = await CreateProductAsync(admin, price: 75m, sellerId);
        var buyer = fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile())).AccessToken);

        (await buyer.PutAsJsonAsync(new Uri($"/api/v1/cart/items/{product.Id}", UriKind.Relative), new { quantity = 2 }))
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

        var awb = (await packed.Content.ReadFromJsonAsync<ShipmentDto>())!.Awb!;
        await CourierAsync(awb, "PICKED UP");

        return new Setup(admin, buyer, seller, product, order, awb);
    }

    /// <summary>An approved seller with a registered address and no owner yet.</summary>
    private async Task<Guid> SellerAsync()
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();
        var id = Guid.NewGuid();

        dbContext.Add(Seller.Seed(
            id,
            new SellerApplication(
                "Return Test Gaushala", null, "9000000000", null, "5 Dairy Lane", null, "Lucknow", "Uttar Pradesh",
                "226001", "Return Test Gaushala", null, "AAAAA0000A", "Return Test Gaushala", "000000000", "SBIN0000000"),
            DateTime.UtcNow));

        await dbContext.SaveChangesAsync();

        return id;
    }

    /// <summary>Links a new account to the seller as its owner and signs in as it.</summary>
    private async Task<HttpClient> SellerAccountAsync(HttpClient admin, Guid sellerId)
    {
        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile(), "Seller Owner");
        var me = await fixture.CreateAuthenticatedClient(tokens.AccessToken)
            .GetFromJsonAsync<UserDto>(new Uri("/api/v1/users/me", UriKind.Relative));

        (await admin.PostAsJsonAsync(new Uri($"/api/v1/admin/sellers/{sellerId}/owner", UriKind.Relative), new { ownerUserId = me!.Id }))
            .EnsureSuccessStatusCode();

        // The SellerOwner role arrives with the next token.
        var refreshed = await (await _auth.RefreshAsync(tokens.RefreshToken)).Content.ReadFromJsonAsync<AuthTokensDto>();

        return fixture.CreateAuthenticatedClient(refreshed!.AccessToken);
    }

    private async Task<HttpClient> AdminClientAsync() =>
        fixture.CreateAuthenticatedClient((await _auth.SignInAsSuperAdminAsync()).AccessToken);

    private static Uri DecisionUri(string who, OrderDto order) =>
        new($"/api/v1/{who}/orders/{order.Id}/parts/{order.Parts.Single().Id}/return-decision", UriKind.Relative);

    private static Task<HttpResponseMessage> RequestReturnAsync(
        HttpClient buyer,
        OrderDto order,
        string reason,
        string? upiId,
        string? comment = null) =>
        buyer.PostAsJsonAsync(
            new Uri($"/api/v1/orders/{order.Id}/parts/{order.Parts.Single().Id}/return", UriKind.Relative),
            new { reason, comment, refundUpiId = upiId });

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

    private static async Task<IReadOnlyList<ShipmentDto>> ShipmentsAsync(HttpClient buyer, OrderDto order) =>
        (await buyer.GetFromJsonAsync<List<ShipmentDto>>(new Uri($"/api/v1/shipping/orders/{order.Id}/shipments", UriKind.Relative)))!;

    private static async Task<IReadOnlyList<RefundDto>> RefundsForAsync(HttpClient admin, Guid orderId)
    {
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
            sku = $"BRT-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
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
