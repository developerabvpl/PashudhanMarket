using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Sellers;

/// <summary>
/// A seller's whole life: apply, be approved, list a product, have it published, receive an
/// order and ship it - and, at each step, be kept away from every other seller's business.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SellerTests(ApiFixture fixture)
{
    private static readonly Uri MeUri = new("/api/v1/sellers/me", UriKind.Relative);
    private static readonly Uri ApplicationUri = new("/api/v1/sellers/me/application", UriKind.Relative);
    private static readonly Uri SellerProductsUri = new("/api/v1/seller/catalog/products", UriKind.Relative);
    private static readonly Uri SellerOrdersUri = new("/api/v1/seller/orders", UriKind.Relative);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task An_applicant_cannot_act_as_a_seller_until_approved_and_then_can_after_refreshing()
    {
        var admin = await AdminClientAsync();
        var (applicant, tokens) = await BuyerAsync();

        (await applicant.GetAsync(MeUri)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var applied = await ApplyAsync(applicant);
        applied.Status.ShouldBe("Pending");
        applied.Kyc.BankAccountLast4.ShouldBe("6789");

        (await applicant.GetAsync(SellerProductsUri)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var queue = await admin.GetFromJsonAsync<PagedList<SellerSummaryDto>>(
            new Uri("/api/v1/admin/sellers?status=Pending&pageSize=100", UriKind.Relative));
        queue!.Items.ShouldContain(s => s.Id == applied.Id);

        // Staff reviewing it see who the owner is, not just an account id.
        var review = await admin.GetFromJsonAsync<SellerDto>(new Uri($"/api/v1/admin/sellers/{applied.Id}", UriKind.Relative));
        review!.OwnerName.ShouldBe("Seller Applicant");

        (await admin.PostAsync(new Uri($"/api/v1/admin/sellers/{applied.Id}/approve", UriKind.Relative), null))
            .EnsureSuccessStatusCode();

        // The old token still carries only buyer permissions; a refresh picks up SellerOwner.
        (await applicant.GetAsync(SellerProductsUri)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var seller = await RefreshedAsync(tokens);

        (await seller.GetAsync(SellerProductsUri)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await seller.GetFromJsonAsync<SellerDto>(MeUri))!.Status.ShouldBe("Approved");
    }

    [DatabaseFact]
    public async Task A_rejected_applicant_sees_why_and_can_resubmit()
    {
        var admin = await AdminClientAsync();
        var (applicant, _) = await BuyerAsync();
        var applied = await ApplyAsync(applicant);

        (await admin.PostAsJsonAsync(
                new Uri($"/api/v1/admin/sellers/{applied.Id}/reject", UriKind.Relative),
                new { note = "The cancelled cheque does not match the account holder." }))
            .EnsureSuccessStatusCode();

        var rejected = await applicant.GetFromJsonAsync<SellerDto>(MeUri);
        rejected!.Status.ShouldBe("Rejected");
        rejected.ReviewNote.ShouldBe("The cancelled cheque does not match the account holder.");

        var resubmitted = await (await applicant.PutAsJsonAsync(ApplicationUri, Application("Gau Seva Kendra")))
            .Content.ReadFromJsonAsync<SellerDto>();

        resubmitted!.Status.ShouldBe("Pending");
        resubmitted.ReviewNote.ShouldBeNull();

        (await applicant.PostAsJsonAsync(ApplicationUri, Application("Second shop"))).StatusCode
            .ShouldBe(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task An_application_with_a_pan_that_does_not_match_its_gstin_is_refused()
    {
        var (applicant, _) = await BuyerAsync();

        var response = await applicant.PostAsJsonAsync(ApplicationUri, Application("Mismatch") with { Gstin = "09ZZZZZ9999Z1Z5" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("PAN does not match");
    }

    [DatabaseFact]
    public async Task A_sellers_draft_goes_live_only_when_a_moderator_publishes_it()
    {
        var admin = await AdminClientAsync();
        var seller = await ApprovedSellerAsync(admin);
        var category = await CategoryAsync(admin);

        var draft = await CreateDraftAsync(seller, category.Id, price: 120m);
        draft.Status.ShouldBe("Draft");

        (await fixture.CreateClient().GetAsync(new Uri($"/api/v1/catalog/products/{draft.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var submitted = await (await seller.PostAsync(SubmitUri(draft.Id), null)).Content.ReadFromJsonAsync<ProductDto>();
        submitted!.Status.ShouldBe("InReview");

        var sentBack = await (await admin.PostAsJsonAsync(
                new Uri($"/api/v1/admin/catalog/products/{draft.Id}/send-back", UriKind.Relative),
                new { note = "Add the pack size to the title." }))
            .Content.ReadFromJsonAsync<ProductDto>();
        sentBack!.Status.ShouldBe("Draft");
        sentBack.ReviewNote.ShouldBe("Add the pack size to the title.");

        (await seller.PutAsJsonAsync(ProductUri(draft.Id), new { name = "Gobar Diya, pack of 12", price = 120m, categoryId = category.Id }))
            .EnsureSuccessStatusCode();
        (await seller.PostAsync(SubmitUri(draft.Id), null)).EnsureSuccessStatusCode();
        (await admin.PostAsync(new Uri($"/api/v1/admin/catalog/products/{draft.Id}/publish", UriKind.Relative), null))
            .EnsureSuccessStatusCode();

        var live = await seller.GetFromJsonAsync<ProductDto>(ProductUri(draft.Id));
        live!.Status.ShouldBe("Active");
        live.ReviewNote.ShouldBeNull();

        // A live listing's wording goes back through a moderator; its price does not.
        (await seller.PutAsJsonAsync(ProductUri(draft.Id), new { name = "Renamed", price = 120m, categoryId = category.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var repriced = await (await seller.PutAsJsonAsync(new Uri($"{ProductUri(draft.Id)}/price", UriKind.Relative), new { price = 99m }))
            .Content.ReadFromJsonAsync<ProductDto>();
        repriced!.Price.ShouldBe(99m);
        repriced.Status.ShouldBe("Active");
    }

    [DatabaseFact]
    public async Task A_seller_cannot_see_or_touch_another_sellers_products_or_stock()
    {
        var admin = await AdminClientAsync();
        var mine = await ApprovedSellerAsync(admin);
        var theirs = await ApprovedSellerAsync(admin);
        var category = await CategoryAsync(admin);
        var theirProduct = await CreateDraftAsync(theirs, category.Id, price: 50m);

        (await mine.GetAsync(ProductUri(theirProduct.Id))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await mine.PostAsync(SubmitUri(theirProduct.Id), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await mine.PutAsJsonAsync(StockUri(theirProduct.Id), new { onHandQuantity = 0 })).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        var myList = await mine.GetFromJsonAsync<PagedList<ProductSummaryDto>>(SellerProductsUri);
        myList!.Items.ShouldNotContain(p => p.Id == theirProduct.Id);

        var counted = await (await theirs.PutAsJsonAsync(StockUri(theirProduct.Id), new { onHandQuantity = 7, reason = "Counted" }))
            .Content.ReadFromJsonAsync<StockLevelDto>();
        counted!.OnHandQuantity.ShouldBe(7);
    }

    [DatabaseFact]
    public async Task A_seller_sees_only_their_part_of_an_order_and_ships_it_from_their_own_pickup_location()
    {
        var admin = await AdminClientAsync();
        var seller = await ApprovedSellerAsync(admin);
        var other = await ApprovedSellerAsync(admin);
        var category = await CategoryAsync(admin);

        var mineProduct = await LiveProductAsync(admin, seller, category.Id, price: 80m);
        var otherProduct = await LiveProductAsync(admin, other, category.Id, price: 30m);

        (await seller.PutAsJsonAsync(new Uri($"{ProductUri(mineProduct.Id)}/package", UriKind.Relative),
                new { weightGrams = 200, lengthCm = 10m, breadthCm = 10m, heightCm = 5m }))
            .EnsureSuccessStatusCode();
        (await seller.PutAsJsonAsync(new Uri("/api/v1/seller/shipping/pickup-location", UriKind.Relative), new { name = "Seller Own Gaushala" }))
            .EnsureSuccessStatusCode();

        // Staff see whose pickup location it is by shop name, not by seller id.
        (await admin.GetFromJsonAsync<List<PickupLocationDto>>(new Uri("/api/v1/admin/shipping/pickup-locations", UriKind.Relative)))!
            .ShouldContain(l => l.Name == "Seller Own Gaushala" && l.ShopName == "Shri Krishna Gaushala");

        var (buyer, _) = await BuyerAsync();
        await AddToCartAsync(buyer, mineProduct.Id, 2);
        await AddToCartAsync(buyer, otherProduct.Id, 1);
        var order = await PlaceCodAsync(buyer);

        var queue = await seller.GetFromJsonAsync<PagedList<SellerOrderSummaryDto>>(SellerOrdersUri);
        var mine = queue!.Items.ShouldHaveSingleItem();
        mine.OrderId.ShouldBe(order.Id);
        mine.Subtotal.ShouldBe(160m);
        mine.CodAmount.ShouldBe(160m);
        mine.ItemCount.ShouldBe(2);

        var detail = await seller.GetFromJsonAsync<SellerOrderDto>(new Uri($"/api/v1/seller/orders/{order.Id}", UriKind.Relative));
        detail!.Lines.ShouldHaveSingleItem().ProductId.ShouldBe(mineProduct.Id);
        detail.DeliveryAddress.Pincode.ShouldBe("226024");

        var otherPart = order.Parts.Single(p => p.Id != mine.PartId);
        (await seller.PostAsJsonAsync(PackUri(order.Id, otherPart.Id), new { })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var shipment = await (await seller.PostAsJsonAsync(PackUri(order.Id, mine.PartId), new { }))
            .Content.ReadFromJsonAsync<ShipmentDto>();

        shipment!.PickupLocation.ShouldBe("Seller Own Gaushala");
        shipment.Parcel.ShouldBe(new ParcelDto(400, 10m, 10m, 10m));

        var shipments = await seller.GetFromJsonAsync<List<ShipmentDto>>(
            new Uri($"/api/v1/seller/shipping/orders/{order.Id}/shipments", UriKind.Relative));
        shipments!.ShouldHaveSingleItem().Id.ShouldBe(shipment.Id);

        (await other.GetFromJsonAsync<List<ShipmentDto>>(
                new Uri($"/api/v1/seller/shipping/orders/{order.Id}/shipments", UriKind.Relative)))!
            .ShouldBeEmpty();
    }

    [DatabaseFact]
    public async Task Staff_link_an_owner_to_a_seller_that_has_none_and_the_owner_can_then_sell()
    {
        var admin = await AdminClientAsync();
        var sellerId = await OwnerlessSellerAsync();
        var (owner, tokens) = await BuyerAsync();
        var me = await owner.GetFromJsonAsync<UserDto>(new Uri("/api/v1/users/me", UriKind.Relative));
        var linkUri = new Uri($"/api/v1/admin/sellers/{sellerId}/owner", UriKind.Relative);

        (await admin.PostAsJsonAsync(linkUri, new { ownerUserId = me!.Id })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var seller = await RefreshedAsync(tokens);
        (await seller.GetFromJsonAsync<SellerDto>(MeUri))!.Id.ShouldBe(sellerId);
        (await seller.GetAsync(SellerProductsUri)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Once owned, it stays owned.
        var (other, _) = await BuyerAsync();
        var otherMe = await other.GetFromJsonAsync<UserDto>(new Uri("/api/v1/users/me", UriKind.Relative));
        (await admin.PostAsJsonAsync(linkUri, new { ownerUserId = otherMe!.Id })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// A seller like the one the sample catalogue is seeded under: approved, with no owner. Made
    /// directly, because the tests do not configure seed sellers.
    /// </summary>
    private async Task<Guid> OwnerlessSellerAsync()
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaar.Infrastructure.Persistence.UPBazaarDbContext>();
        var id = Guid.NewGuid();

        dbContext.Add(UPBazaar.Modules.Sellers.Domain.Seller.Seed(
            id,
            new UPBazaar.Modules.Sellers.Domain.SellerApplication(
                "UP Gaushala Collective", null, "9000000000", null, "To be completed", null, "Lucknow", "Uttar Pradesh",
                "226001", "UP Gaushala Collective", null, "AAAAA0000A", "UP Gaushala Collective", "000000000", "SBIN0000000"),
            DateTime.UtcNow));

        await dbContext.SaveChangesAsync();

        return id;
    }

    private async Task<HttpClient> AdminClientAsync() =>
        fixture.CreateAuthenticatedClient((await _auth.SignInAsSuperAdminAsync()).AccessToken);

    private async Task<(HttpClient Client, AuthTokensDto Tokens)> BuyerAsync()
    {
        var tokens = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile(), "Seller Applicant");

        return (fixture.CreateAuthenticatedClient(tokens.AccessToken), tokens);
    }

    private async Task<HttpClient> RefreshedAsync(AuthTokensDto tokens)
    {
        var refreshed = await (await _auth.RefreshAsync(tokens.RefreshToken)).Content.ReadFromJsonAsync<AuthTokensDto>();

        return fixture.CreateAuthenticatedClient(refreshed!.AccessToken);
    }

    /// <summary>Applies, is approved and refreshes: a seller ready to sell.</summary>
    private async Task<HttpClient> ApprovedSellerAsync(HttpClient admin)
    {
        var (applicant, tokens) = await BuyerAsync();
        var applied = await ApplyAsync(applicant);

        (await admin.PostAsync(new Uri($"/api/v1/admin/sellers/{applied.Id}/approve", UriKind.Relative), null))
            .EnsureSuccessStatusCode();

        return await RefreshedAsync(tokens);
    }

    private static async Task<SellerDto> ApplyAsync(HttpClient applicant)
    {
        var response = await applicant.PostAsJsonAsync(ApplicationUri, Application("Shri Krishna Gaushala"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<SellerDto>())!;
    }

    private static ApplicationBody Application(string shopName) => new(
        shopName,
        "Panchgavya products from our own herd.",
        "9812345678",
        null,
        "Plot 4, Gaushala Marg",
        null,
        "Mathura",
        "Uttar Pradesh",
        "281001",
        "Shri Krishna Gaushala Trust",
        "09ABCDE1234F1Z5",
        "ABCDE1234F",
        "Shri Krishna Gaushala Trust",
        "123456789",
        "SBIN0001234");

    private sealed record ApplicationBody(
        string ShopName,
        string? Description,
        string ContactMobile,
        string? ContactEmail,
        string AddressLine1,
        string? AddressLine2,
        string City,
        string State,
        string Pincode,
        string LegalName,
        string? Gstin,
        string Pan,
        string BankAccountHolder,
        string BankAccountNumber,
        string Ifsc);

    private static async Task<CategoryDto> CategoryAsync(HttpClient admin) =>
        (await (await admin.PostAsJsonAsync(
                new Uri("/api/v1/admin/catalog/categories", UriKind.Relative),
                new { name = $"Category {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<CategoryDto>())!;

    private static async Task<ProductDto> CreateDraftAsync(HttpClient seller, Guid categoryId, decimal price)
    {
        var response = await seller.PostAsJsonAsync(SellerProductsUri, new
        {
            sku = $"SLR-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            name = "Gobar Diya",
            price,
            categoryId,
            onHandQuantity = 10,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    private static async Task<ProductDto> LiveProductAsync(HttpClient admin, HttpClient seller, Guid categoryId, decimal price)
    {
        var draft = await CreateDraftAsync(seller, categoryId, price);

        (await seller.PostAsync(SubmitUri(draft.Id), null)).EnsureSuccessStatusCode();
        (await admin.PostAsync(new Uri($"/api/v1/admin/catalog/products/{draft.Id}/publish", UriKind.Relative), null))
            .EnsureSuccessStatusCode();

        return draft;
    }

    private static async Task AddToCartAsync(HttpClient buyer, Guid productId, int quantity) =>
        (await buyer.PutAsJsonAsync(new Uri($"/api/v1/cart/items/{productId}", UriKind.Relative), new { quantity }))
        .EnsureSuccessStatusCode();

    private static async Task<OrderDto> PlaceCodAsync(HttpClient buyer)
    {
        var response = await buyer.PostAsJsonAsync(new Uri("/api/v1/orders", UriKind.Relative), new
        {
            paymentMethod = "CashOnDelivery",
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

    private static Uri ProductUri(Guid productId) => new($"/api/v1/seller/catalog/products/{productId}", UriKind.Relative);

    private static Uri SubmitUri(Guid productId) => new($"/api/v1/seller/catalog/products/{productId}/submit", UriKind.Relative);

    private static Uri StockUri(Guid productId) => new($"/api/v1/seller/inventory/stock/{productId}", UriKind.Relative);

    private static Uri PackUri(Guid orderId, Guid partId) =>
        new($"/api/v1/seller/shipping/orders/{orderId}/parts/{partId}/pack", UriKind.Relative);
}
