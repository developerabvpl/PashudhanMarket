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
/// Courier costs: quoted when a parcel is booked, charged to the seller when the courier makes the
/// trip, taken off their payouts - and carried forward when there is not yet enough to take them
/// from.
/// </summary>
/// <remarks>
/// The fake courier charges Rs 40 for a parcel up to 500 g, plus Rs 30 to collect cash on delivery.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class CourierCostTests(ApiFixture fixture)
{
    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task A_delivery_is_charged_at_its_quote_and_carried_forward_until_there_is_pay_to_take_it_from()
    {
        var admin = await AdminClientAsync();
        var sellerId = await SellerAsync(admin, pincode: "226001");
        var (_, order) = await PlacedAsync(admin, sellerId);

        var shipment = await PackAsync(admin, order);
        shipment.QuoteError.ShouldBeNull();
        var quoted = shipment.Charges.ShouldHaveSingleItem();
        quoted.Trip.ShouldBe("Delivery");
        quoted.Amount.ShouldBe(70m);
        quoted.Charged.ShouldBe(0m);

        await CourierAsync(shipment.Awb!, "PICKED UP");
        await ProcessOutboxAsync();

        var cost = (await EarningsAsync(admin, sellerId)).ShouldHaveSingleItem();
        cost.Kind.ShouldBe("CourierCost");
        cost.Detail.ShouldBe("Delivery");
        cost.NetAmount.ShouldBe(-70m);

        // The sale is not payable until the return window closes, so this run has only the cost:
        // nothing is paid, and it waits.
        await CourierAsync(shipment.Awb!, "DELIVERED");
        await CourierRemittance.PayAsync(admin, shipment.Awb!, shipment.CodAmount);
        await ProcessOutboxAsync();
        (await RunPayoutsAsync(admin)).SellersCarriedForward.ShouldBeGreaterThanOrEqualTo(1);
        (await PayoutsAsync(admin, sellerId)).ShouldBeEmpty();

        await CloseReturnWindowsAsync(sellerId);
        await RunPayoutsAsync(admin);

        var payout = (await PayoutsAsync(admin, sellerId)).ShouldHaveSingleItem();
        var detail = (await admin.GetFromJsonAsync<PayoutDto>(new Uri($"/api/v1/admin/settlements/payouts/{payout.Id}", UriKind.Relative)))!;
        detail.GrossAmount.ShouldBe(150m);
        detail.CommissionAmount.ShouldBe(15m);
        detail.CourierCostAmount.ShouldBe(70m);
        detail.NetAmount.ShouldBe(65m);
    }

    [DatabaseFact]
    public async Task A_parcel_that_could_not_be_priced_is_charged_once_staff_enter_what_it_cost()
    {
        var admin = await AdminClientAsync();
        var sellerId = await SellerAsync(admin, pincode: null);
        var (_, order) = await PlacedAsync(admin, sellerId);

        var shipment = await PackAsync(admin, order);
        shipment.QuoteError.ShouldNotBeNull();

        await CourierAsync(shipment.Awb!, "PICKED UP");
        await ProcessOutboxAsync();
        (await EarningsAsync(admin, sellerId)).ShouldBeEmpty();

        (await CorrectAsync(admin, shipment, "Delivery", 85m)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CorrectAsync(admin, shipment, "Delivery", 80m)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CorrectAsync(admin, shipment, "ReturnPickup", 10m)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ProcessOutboxAsync();

        (await EarningsAsync(admin, sellerId)).Select(e => e.NetAmount).Order().ShouldBe([-85m, 5m]);
    }

    [DatabaseFact]
    public async Task An_undelivered_parcel_is_charged_for_the_trip_back_as_well()
    {
        var admin = await AdminClientAsync();
        var sellerId = await SellerAsync(admin, pincode: "226001");
        var (_, order) = await PlacedAsync(admin, sellerId);
        var shipment = await PackAsync(admin, order);

        await CourierAsync(shipment.Awb!, "PICKED UP");
        await CourierAsync(shipment.Awb!, "RTO INITIATED");
        await ProcessOutboxAsync();

        var costs = await EarningsAsync(admin, sellerId);
        costs.Single(e => e.Detail == "Delivery").NetAmount.ShouldBe(-70m);
        costs.Single(e => e.Detail == "Rto").NetAmount.ShouldBe(-40m);
    }

    [DatabaseFact]
    public async Task A_return_pickup_is_the_seller_s_cost_unless_the_buyer_just_changed_their_mind()
    {
        var admin = await AdminClientAsync();
        var sellerId = await SellerAsync(admin, pincode: "226001");

        await ReturnedAsync(admin, sellerId, "Damaged");
        (await EarningsAsync(admin, sellerId)).Where(e => e.Detail == "ReturnPickup").ShouldHaveSingleItem().NetAmount.ShouldBe(-40m);

        var other = await SellerAsync(admin, pincode: "226001");
        var buyer = await ReturnedAsync(admin, other, "NoLongerNeeded");
        (await EarningsAsync(admin, other)).ShouldNotContain(e => e.Detail == "ReturnPickup");

        // What the courier charges is between the seller and the platform.
        var tracked = await buyer.Client.GetFromJsonAsync<List<ShipmentDto>>(
            new Uri($"/api/v1/shipping/orders/{buyer.Order.Id}/shipments", UriKind.Relative));
        tracked!.ShouldAllBe(s => s.Charges.Count == 0 && s.QuoteError == null);
    }

    /// <summary>A delivered cash-on-delivery parcel sent back for <paramref name="reason"/>, its pickup made.</summary>
    private async Task<(HttpClient Client, OrderDto Order)> ReturnedAsync(HttpClient admin, Guid sellerId, string reason)
    {
        var (buyer, order) = await PlacedAsync(admin, sellerId);
        var shipment = await PackAsync(admin, order);
        await CourierAsync(shipment.Awb!, "PICKED UP");
        await CourierAsync(shipment.Awb!, "DELIVERED");
        await ProcessOutboxAsync();

        var part = order.Parts.Single();
        (await buyer.PostAsJsonAsync(
                new Uri($"/api/v1/orders/{order.Id}/parts/{part.Id}/return", UriKind.Relative),
                new { reason, refundUpiId = "asha.devi@okicici" }))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync(
                new Uri($"/api/v1/admin/orders/{order.Id}/parts/{part.Id}/return-decision", UriKind.Relative),
                new { approve = true }))
            .EnsureSuccessStatusCode();
        await ProcessOutboxAsync();

        var shipments = await admin.GetFromJsonAsync<List<ShipmentDto>>(
            new Uri($"/api/v1/admin/shipping/orders/{order.Id}/shipments", UriKind.Relative));
        var pickup = shipments!.Single(s => s.Direction == "Return");

        await CourierAsync(pickup.Awb!, "RETURN PICKED UP");
        await ProcessOutboxAsync();

        return (buyer, order);
    }

    private static Task<HttpResponseMessage> CorrectAsync(HttpClient admin, ShipmentDto shipment, string trip, decimal amount) =>
        admin.PutAsJsonAsync(
            new Uri($"/api/v1/admin/shipping/shipments/{shipment.Id}/charges/{trip}", UriKind.Relative),
            new { amount, note = "Shiprocket invoice" });

    /// <summary>Only the seller's courier costs; sales and delivery shares are left out.</summary>
    private static async Task<IReadOnlyList<EarningDto>> EarningsAsync(HttpClient admin, Guid sellerId) =>
        [.. (await admin.GetFromJsonAsync<PagedList<EarningDto>>(
                new Uri($"/api/v1/admin/settlements/earnings?sellerId={sellerId}&pageSize=100", UriKind.Relative)))!
            .Items.Where(e => e.Kind == "CourierCost")];

    private static async Task<IReadOnlyList<PayoutSummaryDto>> PayoutsAsync(HttpClient admin, Guid sellerId) =>
        (await admin.GetFromJsonAsync<PagedList<PayoutSummaryDto>>(
            new Uri($"/api/v1/admin/settlements/payouts?sellerId={sellerId}", UriKind.Relative)))!.Items;

    private static async Task<PayoutRunResultDto> RunPayoutsAsync(HttpClient admin)
    {
        var response = await admin.PostAsync(new Uri("/api/v1/admin/settlements/payout-runs", UriKind.Relative), null);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PayoutRunResultDto>())!;
    }

    /// <summary>Moves the seller's return windows into the past, as a week passing would.</summary>
    private async Task CloseReturnWindowsAsync(Guid sellerId)
    {
        using var scope = fixture.CreateScope();

        await scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>().Set<Earning>()
            .Where(e => e.SellerId == sellerId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PayableFromUtc, DateTime.UtcNow.AddMinutes(-1)));
    }

    private static async Task<ShipmentDto> PackAsync(HttpClient admin, OrderDto order)
    {
        var packed = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/admin/shipping/orders/{order.Id}/parts/{order.Parts.Single().Id}/pack", UriKind.Relative),
            new { parcel = new { weightGrams = 300, lengthCm = 20m, breadthCm = 15m, heightCm = 10m } });
        packed.StatusCode.ShouldBe(HttpStatusCode.OK, await packed.Content.ReadAsStringAsync());

        return (await packed.Content.ReadFromJsonAsync<ShipmentDto>())!;
    }

    /// <summary>Two of a 75-rupee product, cash on delivery, by a new buyer.</summary>
    private async Task<(HttpClient Buyer, OrderDto Order)> PlacedAsync(HttpClient admin, Guid sellerId)
    {
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

        return (buyer, (await placed.Content.ReadFromJsonAsync<OrderDto>())!);
    }

    /// <summary>An approved seller with a bank account, and a pickup location with or without a PIN code.</summary>
    private async Task<Guid> SellerAsync(HttpClient admin, string? pincode)
    {
        var id = Guid.NewGuid();

        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

            dbContext.Add(Seller.Seed(
                id,
                new SellerApplication(
                    "Courier Gaushala", null, "9000000000", null, "5 Dairy Lane", null, "Lucknow", "Uttar Pradesh",
                    "226001", "Courier Gaushala", null, "AAAAA0000A", "Courier Gaushala", "112233445566", "SBIN0001234"),
                DateTime.UtcNow));

            await dbContext.SaveChangesAsync();
        }

        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile(), "Seller Owner");
        var me = await fixture.CreateAuthenticatedClient(tokens.AccessToken).GetFromJsonAsync<UserDto>(new Uri("/api/v1/users/me", UriKind.Relative));

        (await admin.PostAsJsonAsync(new Uri($"/api/v1/admin/sellers/{id}/owner", UriKind.Relative), new { ownerUserId = me!.Id }))
            .EnsureSuccessStatusCode();

        (await admin.PutAsJsonAsync(
                new Uri("/api/v1/admin/shipping/pickup-locations", UriKind.Relative),
                new { sellerId = id, name = "Courier Gaushala", pincode }))
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
            sku = $"CRR-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
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
