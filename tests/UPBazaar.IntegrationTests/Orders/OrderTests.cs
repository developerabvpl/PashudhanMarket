using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Cart.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.Modules.Orders.Services;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Orders;

[Collection(ApiCollection.Name)]
public sealed class OrderTests(ApiFixture fixture)
{
    private static readonly Uri OrdersUri = new("/api/v1/orders", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task Placing_an_order_needs_a_signed_in_buyer()
    {
        (await fixture.CreateClient().PostAsJsonAsync(OrdersUri, PlaceBody("CashOnDelivery")))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task Cash_on_delivery_confirms_the_order_commits_stock_and_empties_the_cart()
    {
        var admin = await AdminClientAsync();
        var sellerA = Guid.NewGuid();
        var diya = await CreateProductAsync(admin, price: 120m, stock: 10, sellerA);
        var sabun = await CreateProductAsync(admin, price: 45.50m, stock: 10, sellerA);
        var dhoop = await CreateProductAsync(admin, price: 60m, stock: 10, Guid.NewGuid());
        var buyer = await BuyerClientAsync();

        await AddToCartAsync(buyer, diya.Id, 2);
        await AddToCartAsync(buyer, sabun.Id, 1);
        await AddToCartAsync(buyer, dhoop.Id, 3);

        var order = await PlaceAsync(buyer, "CashOnDelivery");

        order.Status.ShouldBe("Confirmed");
        order.PaymentStatus.ShouldBe("CashOnDelivery");
        order.Number.ShouldStartWith("UPB-");
        order.Total.ShouldBe(2 * 120m + 45.50m + 3 * 60m);
        order.Parts.Count.ShouldBe(2);
        order.Parts.ShouldAllBe(p => p.Status == "Confirmed");
        order.Parts.Single(p => p.SellerId == sellerA).Lines.Count.ShouldBe(2);
        order.DeliveryAddress.State.ShouldBe("Uttar Pradesh");

        // Read back from the database, a timestamp must still say it is UTC, or a browser shows it
        // five and a half hours early.
        (await GetAsync(buyer, order.Id)).PlacedAtUtc.Kind.ShouldBe(DateTimeKind.Utc);

        (await StockAsync(admin, diya.Id)).ShouldBe(new StockLevelDto(diya.Id, 8, 0, 8));
        (await buyer.GetFromJsonAsync<CartDto>(new Uri("/api/v1/cart", UriKind.Relative)))!.Lines.ShouldBeEmpty();
    }

    [DatabaseFact]
    public async Task A_cart_with_problems_or_nothing_in_it_cannot_be_checked_out()
    {
        var admin = await AdminClientAsync();
        var product = await CreateProductAsync(admin, price: 100m, stock: 5);
        var buyer = await BuyerClientAsync();

        (await buyer.PostAsJsonAsync(OrdersUri, PlaceBody("CashOnDelivery"))).StatusCode
            .ShouldBe(HttpStatusCode.Conflict);

        await AddToCartAsync(buyer, product.Id, 1);
        await RepriceAsync(admin, product, 110m);

        var response = await buyer.PostAsJsonAsync(OrdersUri, PlaceBody("CashOnDelivery"));
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("cart.not_ready_for_checkout");
    }

    [DatabaseFact]
    public async Task A_bad_address_is_refused_field_by_field()
    {
        var buyer = await BuyerClientAsync();

        var response = await buyer.PostAsJsonAsync(OrdersUri, new
        {
            paymentMethod = "CashOnDelivery",
            deliveryAddress = new
            {
                fullName = "Asha Devi",
                mobile = "12345",
                line1 = "12 Gaushala Road",
                city = "Lucknow",
                state = "Narnia",
                pincode = "012345",
            },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("Mobile");
        body.ShouldContain("State");
        body.ShouldContain("Pincode");
    }

    [DatabaseFact]
    public async Task An_online_order_holds_stock_until_paid_then_commits_it()
    {
        var admin = await AdminClientAsync();
        var product = await CreateProductAsync(admin, price: 250m, stock: 5);
        var (buyer, buyerId) = await BuyerWithIdAsync();

        await AddToCartAsync(buyer, product.Id, 2);
        var order = await PlaceAsync(buyer, "Online");

        order.Status.ShouldBe("PendingPayment");
        order.PaymentDueAtUtc.ShouldNotBeNull();
        (await StockAsync(admin, product.Id)).ShouldBe(new StockLevelDto(product.Id, 5, 2, 3));

        var payable = await PaymentsAsync(s => s.GetPayableAsync(order.Id, buyerId, default));
        payable.Value.Amount.ShouldBe(500m);

        // Somebody else's order is invisible to Payments on this buyer's behalf.
        (await PaymentsAsync(s => s.GetPayableAsync(order.Id, Guid.NewGuid(), default))).Error
            .ShouldBe(OrderErrors.NotFound);

        (await PaymentsAsync(s => s.ConfirmPaymentAsync(order.Id, 499m, "pay_short", default))).Error
            .ShouldBe(OrderErrors.AmountMismatch);

        (await PaymentsAsync(s => s.ConfirmPaymentAsync(order.Id, 500m, "pay_1", default))).IsSuccess.ShouldBeTrue();

        // A redelivered webhook changes nothing and commits nothing twice.
        (await PaymentsAsync(s => s.ConfirmPaymentAsync(order.Id, 500m, "pay_1", default))).IsSuccess.ShouldBeTrue();

        var paid = await GetAsync(buyer, order.Id);
        paid.Status.ShouldBe("Confirmed");
        paid.PaymentStatus.ShouldBe("Paid");
        paid.PaymentReference.ShouldBe("pay_1");
        (await StockAsync(admin, product.Id)).ShouldBe(new StockLevelDto(product.Id, 3, 0, 3));
    }

    [DatabaseFact]
    public async Task An_unpaid_order_is_cancelled_at_its_deadline_and_its_stock_released()
    {
        var admin = await AdminClientAsync();
        var product = await CreateProductAsync(admin, price: 80m, stock: 4);
        var (buyer, buyerId) = await BuyerWithIdAsync();

        await AddToCartAsync(buyer, product.Id, 4);
        var order = await PlaceAsync(buyer, "Online");

        await MoveDeadlineIntoThePastAsync(order.Id);

        using (var scope = fixture.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<UnpaidOrderExpiryJob>().CancelUnpaidAsync(default))
                .ShouldBeGreaterThanOrEqualTo(1);
        }

        var cancelled = await GetAsync(buyer, order.Id);
        cancelled.Status.ShouldBe("Cancelled");
        cancelled.CancellationReason.ShouldBe(UnpaidOrderExpiryJob.Reason);
        (await StockAsync(admin, product.Id)).ShouldBe(new StockLevelDto(product.Id, 4, 0, 4));

        // The money that arrives after that must be refunded; Orders says so by refusing it.
        (await PaymentsAsync(s => s.ConfirmPaymentAsync(order.Id, 320m, "pay_late", default))).Error
            .ShouldBe(OrderErrors.NotAwaitingPayment);
        (await PaymentsAsync(s => s.GetPayableAsync(order.Id, buyerId, default))).IsFailure.ShouldBeTrue();
    }

    [DatabaseFact]
    public async Task A_buyer_cancels_before_shipping_and_committed_stock_comes_back()
    {
        var admin = await AdminClientAsync();
        var product = await CreateProductAsync(admin, price: 30m, stock: 6);
        var buyer = await BuyerClientAsync();

        await AddToCartAsync(buyer, product.Id, 5);
        var order = await PlaceAsync(buyer, "CashOnDelivery");
        (await StockAsync(admin, product.Id)).OnHandQuantity.ShouldBe(1);

        var response = await buyer.PostAsJsonAsync(CancelUri(order.Id), new { });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cancelled = (await response.Content.ReadFromJsonAsync<OrderDto>())!;
        cancelled.Status.ShouldBe("Cancelled");
        cancelled.CanCancel.ShouldBeFalse();
        (await StockAsync(admin, product.Id)).ShouldBe(new StockLevelDto(product.Id, 6, 0, 6));
    }

    [DatabaseFact]
    public async Task Once_a_part_ships_the_buyer_can_no_longer_cancel()
    {
        var admin = await AdminClientAsync();
        var product = await CreateProductAsync(admin, price: 30m, stock: 6);
        var buyer = await BuyerClientAsync();

        await AddToCartAsync(buyer, product.Id, 1);
        var order = await PlaceAsync(buyer, "CashOnDelivery");

        (await admin.PostAsJsonAsync(PartUri(order.Id, order.Parts[0].Id, "status"), new { status = "Shipped" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await buyer.PostAsJsonAsync(CancelUri(order.Id), new { })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var delivered = await (await admin.PostAsJsonAsync(
                PartUri(order.Id, order.Parts[0].Id, "status"), new { status = "Delivered" }))
            .Content.ReadFromJsonAsync<OrderDto>();

        delivered!.Status.ShouldBe("Completed");
    }

    [DatabaseFact]
    public async Task Staff_cancel_one_sellers_part_and_only_its_stock_comes_back()
    {
        var admin = await AdminClientAsync();
        var kept = await CreateProductAsync(admin, price: 10m, stock: 5, Guid.NewGuid());
        var dropped = await CreateProductAsync(admin, price: 20m, stock: 5, Guid.NewGuid());
        var buyer = await BuyerClientAsync();

        await AddToCartAsync(buyer, kept.Id, 1);
        await AddToCartAsync(buyer, dropped.Id, 2);
        var order = await PlaceAsync(buyer, "CashOnDelivery");
        var part = order.Parts.Single(p => p.Lines.Any(l => l.ProductId == dropped.Id));

        (await admin.PostAsJsonAsync(PartUri(order.Id, part.Id, "cancel"), new { reason = "" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var after = await (await admin.PostAsJsonAsync(
                PartUri(order.Id, part.Id, "cancel"), new { reason = "Seller cannot supply" }))
            .Content.ReadFromJsonAsync<OrderDto>();

        after!.Status.ShouldBe("Confirmed");
        after.Total.ShouldBe(10m);
        (await StockAsync(admin, dropped.Id)).OnHandQuantity.ShouldBe(5);
        (await StockAsync(admin, kept.Id)).OnHandQuantity.ShouldBe(4);
    }

    [DatabaseFact]
    public async Task A_buyer_sees_only_their_own_orders()
    {
        var admin = await AdminClientAsync();
        var product = await CreateProductAsync(admin, price: 10m, stock: 10);
        var buyer = await BuyerClientAsync();
        var other = await BuyerClientAsync();

        await AddToCartAsync(buyer, product.Id, 1);
        var order = await PlaceAsync(buyer, "CashOnDelivery");

        var mine = await buyer.GetFromJsonAsync<PagedList<OrderSummaryDto>>(OrdersUri);
        mine!.Items.Single().Number.ShouldBe(order.Number);
        mine.Items.Single().Total.ShouldBe(10m);
        mine.Items.Single().ItemCount.ShouldBe(1);
        // The parcels' own statuses come with the row, so the list can say how far they have got.
        mine.Items.Single().PartStatuses.ShouldBe(["Confirmed"]);
        mine.Items.Single().PartReturns.ShouldBe(["None"]);
        // Nothing was paid online, so there is nothing to refund.
        mine.Items.Single().AmountPaid.ShouldBeNull();
        mine.Items.Single().RefundTotal.ShouldBe(0m);

        (await other.GetFromJsonAsync<PagedList<OrderSummaryDto>>(OrdersUri))!.Items.ShouldBeEmpty();
        (await other.GetAsync(new Uri($"/api/v1/orders/{order.Id}", UriKind.Relative))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await other.PostAsJsonAsync(CancelUri(order.Id), new { })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And a buyer cannot reach the staff side at all.
        (await buyer.GetAsync(new Uri("/api/v1/admin/orders", UriKind.Relative))).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var found = await admin.GetFromJsonAsync<PagedList<OrderSummaryDto>>(
            new Uri($"/api/v1/admin/orders?number={order.Number[^6..]}", UriKind.Relative));
        found!.Items.ShouldContain(o => o.Id == order.Id);
    }

    [DatabaseFact]
    public async Task Two_checkouts_of_the_same_cart_place_one_order()
    {
        var admin = await AdminClientAsync();
        var product = await CreateProductAsync(admin, price: 10m, stock: 10);
        var buyer = await BuyerClientAsync();

        await AddToCartAsync(buyer, product.Id, 3);

        var responses = await Task.WhenAll(
            buyer.PostAsJsonAsync(OrdersUri, PlaceBody("CashOnDelivery")),
            buyer.PostAsJsonAsync(OrdersUri, PlaceBody("CashOnDelivery")));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
        (await StockAsync(admin, product.Id)).OnHandQuantity.ShouldBe(7);
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await _auth.SignInAsSuperAdminAsync();

        return fixture.CreateAuthenticatedClient(tokens.AccessToken);
    }

    private async Task<HttpClient> BuyerClientAsync() => (await BuyerWithIdAsync()).Client;

    private async Task<(HttpClient Client, Guid BuyerId)> BuyerWithIdAsync()
    {
        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile());
        var client = fixture.CreateAuthenticatedClient(tokens.AccessToken);
        var me = await client.GetFromJsonAsync<Modules.Identity.Contracts.Dtos.UserDto>(
            new Uri("/api/v1/users/me", UriKind.Relative));

        return (client, me!.Id);
    }

    private async Task<T> PaymentsAsync<T>(Func<IOrderPaymentService, Task<T>> call)
    {
        using var scope = fixture.CreateScope();

        return await call(scope.ServiceProvider.GetRequiredService<IOrderPaymentService>());
    }

    /// <summary>Stands in for fifteen minutes passing, without a clock the tests can wind on.</summary>
    private async Task MoveDeadlineIntoThePastAsync(Guid orderId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        await dbContext.Set<Order>()
            .Where(o => o.PublicId == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentDueAtUtc, DateTime.UtcNow.AddMinutes(-1)));
    }

    private static object PlaceBody(string paymentMethod) => new
    {
        paymentMethod,
        deliveryAddress = new
        {
            fullName = "Asha Devi",
            mobile = "9876543210",
            line1 = "12 Gaushala Road",
            line2 = "Aliganj",
            city = "Lucknow",
            state = "uttar pradesh",
            pincode = "226024",
        },
    };

    private static async Task<OrderDto> PlaceAsync(HttpClient buyer, string paymentMethod)
    {
        var response = await buyer.PostAsJsonAsync(OrdersUri, PlaceBody(paymentMethod));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    private static async Task<OrderDto> GetAsync(HttpClient buyer, Guid orderId) =>
        (await buyer.GetFromJsonAsync<OrderDto>(new Uri($"/api/v1/orders/{orderId}", UriKind.Relative)))!;

    private static async Task AddToCartAsync(HttpClient buyer, Guid productId, int quantity) =>
        (await buyer.PutAsJsonAsync(new Uri($"/api/v1/cart/items/{productId}", UriKind.Relative), new { quantity }))
        .EnsureSuccessStatusCode();

    private static async Task<StockLevelDto> StockAsync(HttpClient admin, Guid productId) =>
        (await admin.GetFromJsonAsync<StockDetailDto>(
            new Uri($"/api/v1/admin/inventory/stock/{productId}", UriKind.Relative)))!.Level;

    private static async Task<ProductDto> CreateProductAsync(
        HttpClient admin,
        decimal price,
        int stock,
        Guid? sellerId = null)
    {
        var category = await (await admin.PostAsJsonAsync(
                new Uri("/api/v1/admin/catalog/categories", UriKind.Relative),
                new { name = $"Category {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<CategoryDto>();

        var response = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/catalog/products", UriKind.Relative), new
        {
            sku = $"ORD-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            name = "Gobar Diya, pack of 12",
            price,
            sellerId = sellerId ?? Guid.NewGuid(),
            categoryId = category!.Id,
            onHandQuantity = stock,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var product = (await response.Content.ReadFromJsonAsync<ProductDto>())!;

        (await admin.PostAsync(new Uri($"/api/v1/admin/catalog/products/{product.Id}/publish", UriKind.Relative), null))
            .EnsureSuccessStatusCode();

        return product;
    }

    private static async Task RepriceAsync(HttpClient admin, ProductDto product, decimal price) =>
        (await admin.PutAsJsonAsync(
            new Uri($"/api/v1/admin/catalog/products/{product.Id}", UriKind.Relative),
            new { name = product.Name, price, categoryId = product.Category.Id }))
        .EnsureSuccessStatusCode();

    private static Uri CancelUri(Guid orderId) => new($"/api/v1/orders/{orderId}/cancel", UriKind.Relative);

    private static Uri PartUri(Guid orderId, Guid partId, string action) =>
        new($"/api/v1/admin/orders/{orderId}/parts/{partId}/{action}", UriKind.Relative);
}
