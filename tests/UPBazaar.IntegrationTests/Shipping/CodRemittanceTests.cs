using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Outbox;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Gateway;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Domain;
using UPBazaar.Modules.Settlements.Contracts.Dtos;
using UPBazaar.Modules.Settlements.Domain;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Shipping;

/// <summary>
/// Cash on delivery: the courier owes what it collected until its remittance report says it has
/// paid, and the seller is paid for a cash-on-delivery parcel only once that cash is in.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CodRemittanceTests(ApiFixture fixture)
{
    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task A_delivered_cod_parcel_is_owed_until_remitted_and_the_seller_waits_for_the_cash()
    {
        var admin = await AdminClientAsync();
        var sellerId = await SellerAsync(admin);
        var (order, awb) = await DeliveredAsync(admin, sellerId, "CashOnDelivery");

        var owed = await ReceivableAsync(admin, awb, "Owed");
        owed.Expected.ShouldBe(150m);
        owed.Status.ShouldBe("Outstanding");
        (await EarningsAsync(admin, sellerId)).Single(e => e.Kind == "Sale").AwaitingCash.ShouldBeTrue();

        // The return window closing is not enough: no cash, no payout.
        await CloseReturnWindowsAsync(sellerId);
        await RunPayoutsAsync(admin);
        (await PayoutsAsync(admin, sellerId)).ShouldBeEmpty();

        var uploaded = await UploadAsync(admin, Reference(), $"AWB,Remitted Amount\n{awb},150\n");
        uploaded.StatusCode.ShouldBe(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync());
        var remittance = (await uploaded.Content.ReadFromJsonAsync<CodRemittanceDto>())!;
        remittance.Total.ShouldBe(150m);
        remittance.Lines.ShouldHaveSingleItem().OrderNumber.ShouldBe(order.Number);
        await ProcessOutboxAsync();

        (await ReceivableAsync(admin, awb, "All")).Status.ShouldBe("Received");
        (await EarningsAsync(admin, sellerId)).Single(e => e.Kind == "Sale").AwaitingCash.ShouldBeFalse();

        await RunPayoutsAsync(admin);
        (await PayoutsAsync(admin, sellerId)).ShouldHaveSingleItem().GrossAmount.ShouldBe(150m);
    }

    [DatabaseFact]
    public async Task A_row_paid_before_the_delivery_update_is_matched_when_it_arrives()
    {
        var admin = await AdminClientAsync();
        var sellerId = await SellerAsync(admin);
        var (order, awb) = await ShippedAsync(admin, sellerId, "CashOnDelivery");

        var remittance = (await (await UploadAsync(admin, Reference(), $"AWB,Remitted Amount\n{awb},150\n")).Content.ReadFromJsonAsync<CodRemittanceDto>())!;
        remittance.UnmatchedCount.ShouldBe(1);

        await CourierAsync(awb, "DELIVERED");
        await ProcessOutboxAsync();

        (await ReceivableAsync(admin, awb, "All")).Status.ShouldBe("Received");
        (await EarningsAsync(admin, sellerId)).Single(e => e.Kind == "Sale").AwaitingCash.ShouldBeFalse();

        var detail = await admin.GetFromJsonAsync<CodRemittanceDto>(new Uri($"/api/v1/admin/shipping/cod/remittances/{remittance.Id}", UriKind.Relative));
        detail!.Lines.ShouldHaveSingleItem().OrderNumber.ShouldBe(order.Number);
    }

    [DatabaseFact]
    public async Task A_short_payment_is_flagged_and_writing_it_off_releases_the_seller_s_pay()
    {
        var admin = await AdminClientAsync();
        var sellerId = await SellerAsync(admin);
        var (_, awb) = await DeliveredAsync(admin, sellerId, "CashOnDelivery");
        var reference = Reference();

        (await UploadAsync(admin, reference, $"AWB,Remitted Amount\n{awb},140\n")).EnsureSuccessStatusCode();
        (await UploadAsync(admin, reference, $"AWB,Remitted Amount\n{awb},10\n")).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var shortPaid = await ReceivableAsync(admin, awb, "Short");
        shortPaid.Received.ShouldBe(140m);
        (await EarningsAsync(admin, sellerId)).Single(e => e.Kind == "Sale").AwaitingCash.ShouldBeTrue();

        var written = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/admin/shipping/cod/receivables/{shortPaid.Id}/write-off", UriKind.Relative),
            new { note = "Courier confirmed a ₹10 cash shortage at the hub." });
        written.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await written.Content.ReadFromJsonAsync<CodReceivableDto>())!.Status.ShouldBe("WrittenOff");
        await ProcessOutboxAsync();

        (await EarningsAsync(admin, sellerId)).Single(e => e.Kind == "Sale").AwaitingCash.ShouldBeFalse();
    }

    [DatabaseFact]
    public async Task A_prepaid_parcel_owes_no_cash_and_an_unreadable_report_is_refused()
    {
        var admin = await AdminClientAsync();
        var sellerId = await SellerAsync(admin);
        var (_, awb) = await DeliveredAsync(admin, sellerId, "Online");

        var all = await admin.GetFromJsonAsync<PagedList<CodReceivableDto>>(new Uri("/api/v1/admin/shipping/cod/receivables?filter=All&pageSize=100", UriKind.Relative));
        all!.Items.ShouldNotContain(r => r.Awb == awb);
        (await EarningsAsync(admin, sellerId)).Single(e => e.Kind == "Sale").AwaitingCash.ShouldBeFalse();

        (await UploadAsync(admin, Reference(), "Order,Total\nUPB-1,10\n")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static string Reference() => $"UTR{Guid.NewGuid():N}"[..16].ToUpperInvariant();

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient admin, string reference, string csv)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(reference), "reference");
        form.Add(new StringContent("2026-09-25"), "remittedOn");

        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "remittance.csv");

        return await admin.PostAsync(new Uri("/api/v1/admin/shipping/cod/remittances", UriKind.Relative), form);
    }

    private static async Task<CodReceivableDto> ReceivableAsync(HttpClient admin, string awb, string filter)
    {
        var page = await admin.GetFromJsonAsync<PagedList<CodReceivableDto>>(
            new Uri($"/api/v1/admin/shipping/cod/receivables?filter={filter}&pageSize=100", UriKind.Relative));

        return page!.Items.Single(r => r.Awb == awb);
    }

    private async Task<(OrderDto Order, string Awb)> DeliveredAsync(HttpClient admin, Guid sellerId, string paymentMethod)
    {
        var (order, awb) = await ShippedAsync(admin, sellerId, paymentMethod);

        await CourierAsync(awb, "DELIVERED");
        await ProcessOutboxAsync();

        return (order, awb);
    }

    /// <summary>A new buyer's order for two of a 75-rupee product, packed and picked up.</summary>
    private async Task<(OrderDto Order, string Awb)> ShippedAsync(HttpClient admin, Guid sellerId, string paymentMethod)
    {
        var product = await CreateProductAsync(admin, sellerId);
        var buyer = fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile())).AccessToken);

        (await buyer.PutAsJsonAsync(new Uri($"/api/v1/cart/items/{product.Id}", UriKind.Relative), new { quantity = 2 }))
            .EnsureSuccessStatusCode();

        var placed = await buyer.PostAsJsonAsync(new Uri("/api/v1/orders", UriKind.Relative), new
        {
            paymentMethod,
            deliveryAddress = new { fullName = "Asha Devi", mobile = "9876543210", line1 = "12 Gaushala Road", city = "Lucknow", state = "Uttar Pradesh", pincode = "226024" },
        });
        placed.StatusCode.ShouldBe(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var order = (await placed.Content.ReadFromJsonAsync<OrderDto>())!;

        if (paymentMethod == "Online")
        {
            await PayAsync(buyer, order);
        }

        var packed = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/admin/shipping/orders/{order.Id}/parts/{order.Parts.Single().Id}/pack", UriKind.Relative),
            new { parcel = new { weightGrams = 300, lengthCm = 20m, breadthCm = 15m, heightCm = 10m } });
        packed.StatusCode.ShouldBe(HttpStatusCode.OK, await packed.Content.ReadAsStringAsync());
        var awb = (await packed.Content.ReadFromJsonAsync<ShipmentDto>())!.Awb!;

        await CourierAsync(awb, "PICKED UP");

        return (order, awb);
    }

    private async Task PayAsync(HttpClient buyer, OrderDto order)
    {
        var session = await (await buyer.PostAsync(new Uri($"/api/v1/payments/orders/{order.Id}/checkout", UriKind.Relative), null))
            .Content.ReadFromJsonAsync<CheckoutSessionDto>();
        var paymentId = $"pay_{Guid.NewGuid():N}"[..20];

        (await buyer.PostAsJsonAsync(new Uri("/api/v1/payments/razorpay/verify", UriKind.Relative), new
        {
            gatewayOrderId = session!.GatewayOrderId,
            gatewayPaymentId = paymentId,
            signature = RazorpaySignature.ForPayment(session.GatewayOrderId, paymentId, FakeGateway.KeySecret),
        })).EnsureSuccessStatusCode();
        await ProcessOutboxAsync();
    }

    private static async Task<IReadOnlyList<PayoutSummaryDto>> PayoutsAsync(HttpClient admin, Guid sellerId) =>
        (await admin.GetFromJsonAsync<PagedList<PayoutSummaryDto>>(
            new Uri($"/api/v1/admin/settlements/payouts?sellerId={sellerId}", UriKind.Relative)))!.Items;

    private static async Task RunPayoutsAsync(HttpClient admin) =>
        (await admin.PostAsync(new Uri("/api/v1/admin/settlements/payout-runs", UriKind.Relative), null)).EnsureSuccessStatusCode();

    /// <summary>Moves the seller's return windows into the past, as a week passing would.</summary>
    private async Task CloseReturnWindowsAsync(Guid sellerId)
    {
        using var scope = fixture.CreateScope();

        await scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>().Set<Earning>()
            .Where(e => e.SellerId == sellerId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PayableFromUtc, DateTime.UtcNow.AddMinutes(-1)));
    }

    private static async Task<IReadOnlyList<EarningDto>> EarningsAsync(HttpClient admin, Guid sellerId) =>
        (await admin.GetFromJsonAsync<PagedList<EarningDto>>(
            new Uri($"/api/v1/admin/settlements/earnings?sellerId={sellerId}", UriKind.Relative)))!.Items;

    /// <summary>An approved seller with a pickup location.</summary>
    private async Task<Guid> SellerAsync(HttpClient admin)
    {
        var id = Guid.NewGuid();

        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

            dbContext.Add(Seller.Seed(
                id,
                new SellerApplication(
                    "Cash Gaushala", null, "9000000000", null, "5 Dairy Lane", null, "Lucknow", "Uttar Pradesh",
                    "226001", "Cash Gaushala", null, "AAAAA0000A", "Cash Gaushala", "112233445566", "SBIN0001234"),
                DateTime.UtcNow));

            await dbContext.SaveChangesAsync();
        }

        (await admin.PutAsJsonAsync(new Uri("/api/v1/admin/shipping/pickup-locations", UriKind.Relative), new { sellerId = id, name = "Cash Gaushala" }))
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

    private static async Task<ProductDto> CreateProductAsync(HttpClient admin, Guid sellerId)
    {
        var category = await (await admin.PostAsJsonAsync(
                new Uri("/api/v1/admin/catalog/categories", UriKind.Relative),
                new { name = $"Category {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<CategoryDto>();

        var response = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/catalog/products", UriKind.Relative), new
        {
            sku = $"COD-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
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
