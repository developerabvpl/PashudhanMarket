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
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Domain;
using UPBazaar.Modules.Settlements.Contracts.Dtos;
using UPBazaar.Modules.Settlements.Domain;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Settlements;

/// <summary>
/// Paying sellers: a delivered parcel earns, the return window closes, the run makes a payout,
/// finance record the transfer - and returns hold or cancel what was earned.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SettlementTests(ApiFixture fixture)
{
    private const string AccountNumber = "112233445566";

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task A_delivered_parcel_is_paid_out_after_its_return_window_at_the_sellers_own_commission()
    {
        var admin = await AdminClientAsync();
        var (sellerId, seller) = await SellerAsync(admin);

        (await admin.PutAsJsonAsync(new Uri($"/api/v1/admin/settlements/commissions/{sellerId}", UriKind.Relative), new { commissionPercent = 8m }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var order = await DeliveredAsync(admin, sellerId);
        await ProcessOutboxAsync();

        var earning = (await EarningsAsync(admin, sellerId)).ShouldHaveSingleItem();
        earning.OrderId.ShouldBe(order.Id);
        earning.GrossAmount.ShouldBe(150m);
        earning.CommissionPercent.ShouldBe(8m);
        earning.NetAmount.ShouldBe(138m);
        earning.Status.ShouldBe("Accruing");

        // Still inside the buyer's return window: the run leaves it alone.
        await RunPayoutsAsync(admin);
        (await PayoutsAsync(admin, sellerId)).ShouldBeEmpty();
        (await seller.GetFromJsonAsync<SellerBalanceDto>(BalanceUri))!.AccruingAmount.ShouldBe(138m);

        await CloseReturnWindowsAsync(sellerId);
        (await seller.GetFromJsonAsync<SellerBalanceDto>(BalanceUri))!.PayableAmount.ShouldBe(138m);

        await RunPayoutsAsync(admin);

        var summary = (await PayoutsAsync(admin, sellerId)).ShouldHaveSingleItem();
        summary.NetAmount.ShouldBe(138m);
        summary.Status.ShouldBe("Pending");

        // Finance see the account whole to pay it; the seller sees it masked.
        var forFinance = await admin.GetFromJsonAsync<PayoutDto>(new Uri($"/api/v1/admin/settlements/payouts/{summary.Id}", UriKind.Relative));
        forFinance!.AccountNumber.ShouldBe(AccountNumber);
        forFinance.Earnings.ShouldHaveSingleItem().Status.ShouldBe("Settled");

        var forSeller = await seller.GetFromJsonAsync<PayoutDto>(new Uri($"/api/v1/seller/settlements/payouts/{summary.Id}", UriKind.Relative));
        forSeller!.AccountNumber.ShouldEndWith("5566");
        forSeller.AccountNumber.ShouldNotContain("1122");

        // Running again finds nothing new to pay.
        await RunPayoutsAsync(admin);
        (await PayoutsAsync(admin, sellerId)).Count.ShouldBe(1);

        var paid = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/admin/settlements/payouts/{summary.Id}/mark-paid", UriKind.Relative), new { utr = "N123456789012345" });
        paid.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await paid.Content.ReadFromJsonAsync<PayoutDto>())!.Utr.ShouldBe("N123456789012345");

        (await admin.PostAsJsonAsync(new Uri($"/api/v1/admin/settlements/payouts/{summary.Id}/mark-paid", UriKind.Relative), new { utr = "AGAIN" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var balance = await seller.GetFromJsonAsync<SellerBalanceDto>(BalanceUri);
        balance!.PaidAmount.ShouldBe(138m);
        balance.PayableAmount.ShouldBe(0m);
    }

    [DatabaseFact]
    public async Task A_return_request_holds_the_earning_and_an_accepted_return_cancels_it()
    {
        var admin = await AdminClientAsync();
        var (sellerId, _) = await SellerAsync(admin);
        var order = await DeliveredAsync(admin, sellerId);
        var part = order.Parts.Single();

        (await Buyer(order).PostAsJsonAsync(
                new Uri($"/api/v1/orders/{order.Id}/parts/{part.Id}/return", UriKind.Relative),
                new { reason = "Damaged", refundUpiId = "asha@okicici" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await ProcessOutboxAsync();

        (await EarningsAsync(admin, sellerId)).Single().Status.ShouldBe("OnHold");

        // Even past the window, a return still being decided is not paid.
        await CloseReturnWindowsAsync(sellerId);
        await RunPayoutsAsync(admin);
        (await PayoutsAsync(admin, sellerId)).ShouldBeEmpty();

        (await admin.PostAsJsonAsync(new Uri($"/api/v1/admin/orders/{order.Id}/parts/{part.Id}/return-decision", UriKind.Relative), new { approve = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await ProcessOutboxAsync();

        (await EarningsAsync(admin, sellerId)).Single().Status.ShouldBe("Cancelled");
        await RunPayoutsAsync(admin);
        (await PayoutsAsync(admin, sellerId)).ShouldBeEmpty();
    }

    [DatabaseFact]
    public async Task A_refused_return_releases_the_earning_to_be_paid()
    {
        var admin = await AdminClientAsync();
        var (sellerId, _) = await SellerAsync(admin);
        var order = await DeliveredAsync(admin, sellerId);
        var part = order.Parts.Single();

        await Buyer(order).PostAsJsonAsync(
            new Uri($"/api/v1/orders/{order.Id}/parts/{part.Id}/return", UriKind.Relative),
            new { reason = "NoLongerNeeded", refundUpiId = "asha@okicici" });
        (await admin.PostAsJsonAsync(
                new Uri($"/api/v1/admin/orders/{order.Id}/parts/{part.Id}/return-decision", UriKind.Relative),
                new { approve = false, note = "Opened food cannot be taken back." }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await ProcessOutboxAsync();

        (await EarningsAsync(admin, sellerId)).Single().Status.ShouldBe("Accruing");

        await CloseReturnWindowsAsync(sellerId);
        await RunPayoutsAsync(admin);
        (await PayoutsAsync(admin, sellerId)).ShouldHaveSingleItem();
    }

    [DatabaseFact]
    public async Task Rates_that_would_leave_a_seller_nothing_are_refused_and_only_finance_may_change_them()
    {
        var admin = await AdminClientAsync();
        var policyUri = new Uri("/api/v1/admin/settlements/policy", UriKind.Relative);
        var before = await admin.GetFromJsonAsync<SettlementPolicyDto>(policyUri);

        (await admin.PutAsJsonAsync(policyUri, new { defaultCommissionPercent = 90m, tcsPercent = 5m, tdsPercent = 5m }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync(policyUri, new { defaultCommissionPercent = 10.555m, tcsPercent = 0m, tdsPercent = 0m }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var changed = await admin.PutAsJsonAsync(policyUri, new { defaultCommissionPercent = before!.DefaultCommissionPercent, tcsPercent = 0.5m, tdsPercent = 0.1m });
        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await changed.Content.ReadFromJsonAsync<SettlementPolicyDto>())!.TcsPercent.ShouldBe(0.5m);

        // Put back, so other tests earn at the defaults.
        (await admin.PutAsJsonAsync(policyUri, new { before.DefaultCommissionPercent, before.TcsPercent, before.TdsPercent }))
            .EnsureSuccessStatusCode();

        var buyer = fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile())).AccessToken);
        (await buyer.GetAsync(policyUri)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await buyer.PostAsync(new Uri("/api/v1/admin/settlements/payout-runs", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await buyer.GetAsync(BalanceUri)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static readonly Uri BalanceUri = new("/api/v1/seller/settlements/balance", UriKind.Relative);

    private readonly Dictionary<Guid, HttpClient> _buyers = [];

    private HttpClient Buyer(OrderDto order) => _buyers[order.Id];

    /// <summary>Two of a 75-rupee product, cash on delivery, delivered.</summary>
    private async Task<OrderDto> DeliveredAsync(HttpClient admin, Guid sellerId)
    {
        (await admin.PutAsJsonAsync(new Uri("/api/v1/admin/shipping/pickup-locations", UriKind.Relative), new { sellerId, name = "Settlement Gaushala" }))
            .EnsureSuccessStatusCode();

        var product = await CreateProductAsync(admin, sellerId);
        var buyer = fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile())).AccessToken);

        (await buyer.PutAsJsonAsync(new Uri($"/api/v1/cart/items/{product.Id}", UriKind.Relative), new { quantity = 2 }))
            .EnsureSuccessStatusCode();

        var placed = await buyer.PostAsJsonAsync(new Uri("/api/v1/orders", UriKind.Relative), new
        {
            paymentMethod = "CashOnDelivery",
            deliveryAddress = new { fullName = "Asha Devi", mobile = "9876543210", line1 = "12 Gaushala Road", city = "Lucknow", state = "Uttar Pradesh", pincode = "226024" },
        });
        placed.StatusCode.ShouldBe(HttpStatusCode.Created);
        var order = (await placed.Content.ReadFromJsonAsync<OrderDto>())!;

        var packed = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/admin/shipping/orders/{order.Id}/parts/{order.Parts.Single().Id}/pack", UriKind.Relative),
            new { parcel = new { weightGrams = 300, lengthCm = 20m, breadthCm = 15m, heightCm = 10m } });
        packed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var awb = (await packed.Content.ReadFromJsonAsync<ShipmentDto>())!.Awb!;
        await CourierAsync(awb, "PICKED UP");
        await CourierAsync(awb, "DELIVERED");

        _buyers[order.Id] = buyer;

        return (await buyer.GetFromJsonAsync<OrderDto>(new Uri($"/api/v1/orders/{order.Id}", UriKind.Relative)))!;
    }

    /// <summary>An approved seller with a bank account and an owner account signed in.</summary>
    private async Task<(Guid Id, HttpClient Client)> SellerAsync(HttpClient admin)
    {
        var id = Guid.NewGuid();

        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

            dbContext.Add(Seller.Seed(
                id,
                new SellerApplication(
                    "Settlement Gaushala", null, "9000000000", null, "5 Dairy Lane", null, "Lucknow", "Uttar Pradesh",
                    "226001", "Settlement Gaushala", null, "AAAAA0000A", "Settlement Gaushala", AccountNumber, "SBIN0001234"),
                DateTime.UtcNow));

            await dbContext.SaveChangesAsync();
        }

        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile(), "Seller Owner");
        var me = await fixture.CreateAuthenticatedClient(tokens.AccessToken).GetFromJsonAsync<UserDto>(new Uri("/api/v1/users/me", UriKind.Relative));

        (await admin.PostAsJsonAsync(new Uri($"/api/v1/admin/sellers/{id}/owner", UriKind.Relative), new { ownerUserId = me!.Id }))
            .EnsureSuccessStatusCode();

        var refreshed = await (await _auth.RefreshAsync(tokens.RefreshToken)).Content.ReadFromJsonAsync<AuthTokensDto>();

        return (id, fixture.CreateAuthenticatedClient(refreshed!.AccessToken));
    }

    /// <summary>Moves the seller's return windows into the past, as a week passing would.</summary>
    private async Task CloseReturnWindowsAsync(Guid sellerId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        await dbContext.Set<Earning>()
            .Where(e => e.SellerId == sellerId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PayableFromUtc, DateTime.UtcNow.AddMinutes(-1)));
    }

    private static async Task RunPayoutsAsync(HttpClient admin) =>
        (await admin.PostAsync(new Uri("/api/v1/admin/settlements/payout-runs", UriKind.Relative), null)).EnsureSuccessStatusCode();

    private static async Task<IReadOnlyList<EarningDto>> EarningsAsync(HttpClient admin, Guid sellerId) =>
        (await admin.GetFromJsonAsync<PagedList<EarningDto>>(new Uri($"/api/v1/admin/settlements/earnings?sellerId={sellerId}", UriKind.Relative)))!.Items;

    private static async Task<IReadOnlyList<PayoutSummaryDto>> PayoutsAsync(HttpClient admin, Guid sellerId) =>
        (await admin.GetFromJsonAsync<PagedList<PayoutSummaryDto>>(new Uri($"/api/v1/admin/settlements/payouts?sellerId={sellerId}", UriKind.Relative)))!.Items;

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
            sku = $"STL-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
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
