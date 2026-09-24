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
using UPBazaar.Modules.Notifications.Domain;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Domain;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Outbox;

namespace UPBazaar.IntegrationTests.Notifications;

/// <summary>
/// Who hears about what: buyers by text as their order moves, sellers by email when there is
/// work to do. Tests run with the development senders, so each message is checked in the log
/// table the notifier keeps.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationTests(ApiFixture fixture)
{
    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task A_buyer_is_texted_as_their_order_moves_and_the_seller_is_emailed_to_pack_it()
    {
        var admin = await AdminClientAsync();
        var sellerEmail = $"shop-{Guid.NewGuid():N}@example.test";
        var sellerId = await SellerAsync(sellerEmail);
        var buyerMobile = AuthClient.NewMobile();

        var order = await PlaceAsync(admin, sellerId, buyerMobile);
        await ProcessOutboxAsync();

        var confirmed = await MessageAsync("order-confirmed", buyerMobile);
        confirmed.Status.ShouldBe(NotificationStatus.Sent);
        confirmed.Channel.ShouldBe(NotificationChannel.Sms);
        confirmed.Body.ShouldContain(order.Number);
        confirmed.Body.ShouldContain("Rs. 150");
        confirmed.Body.ShouldContain($"/orders/{order.Id}");

        var toPack = await MessageAsync("order-to-pack", sellerEmail);
        toPack.Status.ShouldBe(NotificationStatus.Sent);
        toPack.Subject.ShouldBe($"New order {order.Number} to pack");
        toPack.Body.ShouldContain("cash on delivery");

        var awb = await PackAsync(admin, order);
        await CourierAsync(awb, "PICKED UP");
        await CourierAsync(awb, "IN TRANSIT");
        await CourierAsync(awb, "DELIVERED");
        await ProcessOutboxAsync();

        // Told once that it is on its way, however many scans follow.
        var dispatched = await MessagesAsync("parcel-dispatched", buyerMobile);
        dispatched.ShouldHaveSingleItem().Body.ShouldContain($"AWB {awb}");

        (await MessageAsync("parcel-delivered", buyerMobile)).Body.ShouldContain("You can ask to return it until");
    }

    [DatabaseFact]
    public async Task An_event_delivered_twice_sends_its_message_once()
    {
        var admin = await AdminClientAsync();
        var buyerMobile = AuthClient.NewMobile();
        var order = await PlaceAsync(admin, await SellerAsync($"shop-{Guid.NewGuid():N}@example.test"), buyerMobile);

        await ProcessOutboxAsync();
        await ReplayOutboxAsync(order.Number);
        await ProcessOutboxAsync();

        (await MessagesAsync("order-confirmed", buyerMobile)).ShouldHaveSingleItem();
    }

    [DatabaseFact]
    public async Task A_seller_with_no_email_is_skipped_and_recorded_as_such()
    {
        var admin = await AdminClientAsync();
        var order = await PlaceAsync(admin, await SellerAsync(email: null), AuthClient.NewMobile());
        await ProcessOutboxAsync();

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();
        var skipped = await dbContext.Set<NotificationMessage>()
            .AsNoTracking()
            .Where(m => m.Template == "order-to-pack" && m.Subject == $"New order {order.Number} to pack")
            .SingleAsync();

        skipped.Status.ShouldBe(NotificationStatus.Skipped);
        skipped.Error.ShouldBe("No email address.");
    }

    private async Task<NotificationMessage> MessageAsync(string template, string recipient) =>
        (await MessagesAsync(template, recipient)).ShouldHaveSingleItem();

    private async Task<IReadOnlyList<NotificationMessage>> MessagesAsync(string template, string recipient)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        return await dbContext.Set<NotificationMessage>()
            .AsNoTracking()
            .Where(m => m.Template == template && m.Recipient == recipient)
            .ToListAsync();
    }

    /// <summary>Puts the order's already-processed events back in the queue, as a crash before acknowledging would.</summary>
    private async Task ReplayOutboxAsync(string orderNumber)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        await dbContext.Set<OutboxMessage>()
            .Where(m => m.Payload.Contains(orderNumber))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAtUtc, (DateTime?)null));
    }

    /// <summary>An approved seller whose shop email is the one given, or none.</summary>
    private async Task<Guid> SellerAsync(string? email)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();
        var id = Guid.NewGuid();

        dbContext.Add(Seller.Seed(
            id,
            new SellerApplication(
                "Notify Gaushala", null, "9000000000", email, "5 Dairy Lane", null, "Lucknow", "Uttar Pradesh",
                "226001", "Notify Gaushala", null, "AAAAA0000A", "Notify Gaushala", "112233445566", "SBIN0001234"),
            DateTime.UtcNow));

        await dbContext.SaveChangesAsync();

        return id;
    }

    /// <summary>Two of a 75-rupee product, cash on delivery, from the given seller, by a buyer with this mobile.</summary>
    private async Task<OrderDto> PlaceAsync(HttpClient admin, Guid sellerId, string buyerMobile)
    {
        (await admin.PutAsJsonAsync(new Uri("/api/v1/admin/shipping/pickup-locations", UriKind.Relative), new { sellerId, name = "Notify Gaushala" }))
            .EnsureSuccessStatusCode();

        var product = await CreateProductAsync(admin, sellerId);
        var buyer = fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(buyerMobile)).AccessToken);

        (await buyer.PutAsJsonAsync(new Uri($"/api/v1/cart/items/{product.Id}", UriKind.Relative), new { quantity = 2 }))
            .EnsureSuccessStatusCode();

        var placed = await buyer.PostAsJsonAsync(new Uri("/api/v1/orders", UriKind.Relative), new
        {
            paymentMethod = "CashOnDelivery",
            deliveryAddress = new { fullName = "Asha Devi", mobile = "9876543210", line1 = "12 Gaushala Road", city = "Lucknow", state = "Uttar Pradesh", pincode = "226024" },
        });
        placed.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await placed.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    private static async Task<string> PackAsync(HttpClient admin, OrderDto order)
    {
        var packed = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/admin/shipping/orders/{order.Id}/parts/{order.Parts.Single().Id}/pack", UriKind.Relative),
            new { parcel = new { weightGrams = 300, lengthCm = 20m, breadthCm = 15m, heightCm = 10m } });
        packed.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await packed.Content.ReadFromJsonAsync<ShipmentDto>())!.Awb!;
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
            sku = $"NTF-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
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
