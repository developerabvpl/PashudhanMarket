using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Outbox;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Identity.Contracts.Dtos;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Reviews.Contracts.Dtos;
using UPBazaar.Modules.Sellers.Domain;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.IntegrationTests.Reviews;

/// <summary>
/// Reviews: a delivered buyer rates at once, staff approve the words and photos, the seller
/// replies, and staff can hide the reply. Returning the parcel does not take the right away.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReviewTests(ApiFixture fixture)
{
    /// <summary>A 1x1 PNG: the smallest real image.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task Stars_count_at_once_while_the_words_wait_for_staff()
    {
        var admin = await AdminClientAsync();
        var (sellerId, _) = await SellerAsync(admin);
        var (buyer, order) = await DeliveredAsync(admin, sellerId);
        var productId = order.Parts.Single().Lines.Single().ProductId;

        var mine = await MineAsync(buyer, productId);
        mine.CanReview.ShouldBeTrue();
        mine.Review.ShouldBeNull();

        var saved = await SaveAsync(buyer, productId, 4, "Burns clean", "No smoke at all, lasts the evening.");
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var review = (await saved.Content.ReadFromJsonAsync<ReviewDto>())!;
        review.ContentStatus.ShouldBe("Pending");
        review.ReviewerName.ShouldBe("Asha Devi");

        var shown = await ProductReviewsAsync(productId);
        shown.Summary.Average.ShouldBe(4m);
        shown.Summary.Count.ShouldBe(1);
        shown.Summary.Stars.ShouldBe([0, 0, 0, 1, 0]);
        shown.Items.Single().Title.ShouldBeNull();
        shown.Items.Single().Body.ShouldBeNull();

        (await admin.PostAsync(new Uri($"/api/v1/admin/reviews/{review.Id}/approve", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ProductReviewsAsync(productId)).Items.Single().Title.ShouldBe("Burns clean");

        // Changing the words sends them back; the new stars count straight away.
        (await SaveAsync(buyer, productId, 2, "Burns clean", "Cracked after a week.")).EnsureSuccessStatusCode();

        shown = await ProductReviewsAsync(productId);
        shown.Summary.Average.ShouldBe(2m);
        shown.Summary.Count.ShouldBe(1);
        shown.Items.Single().Body.ShouldBeNull();
        (await MineAsync(buyer, productId)).Review!.ContentStatus.ShouldBe("Pending");

        // Approving twice finds nothing waiting the second time.
        (await admin.PostAsync(new Uri($"/api/v1/admin/reviews/{review.Id}/approve", UriKind.Relative), null)).EnsureSuccessStatusCode();
        (await admin.PostAsync(new Uri($"/api/v1/admin/reviews/{review.Id}/approve", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task Rejected_words_stay_hidden_but_the_stars_still_count()
    {
        var admin = await AdminClientAsync();
        var (sellerId, _) = await SellerAsync(admin);
        var (buyer, order) = await DeliveredAsync(admin, sellerId);
        var productId = order.Parts.Single().Lines.Single().ProductId;

        var review = (await (await SaveAsync(buyer, productId, 1, null, "Call me on 9876543210 for a better deal"))
            .Content.ReadFromJsonAsync<ReviewDto>())!;

        var queue = await admin.GetFromJsonAsync<PagedList<ReviewDto>>(
            new Uri("/api/v1/admin/reviews?status=Pending&pageSize=100", UriKind.Relative));
        queue!.Items.ShouldContain(r => r.Id == review.Id && r.Body != null);

        (await admin.PostAsJsonAsync(new Uri($"/api/v1/admin/reviews/{review.Id}/reject", UriKind.Relative), new { note = "" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync(
                new Uri($"/api/v1/admin/reviews/{review.Id}/reject", UriKind.Relative),
                new { note = "Please leave out phone numbers." }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var mine = (await MineAsync(buyer, productId)).Review!;
        mine.ContentStatus.ShouldBe("Rejected");
        mine.ModerationNote.ShouldBe("Please leave out phone numbers.");

        var shown = await ProductReviewsAsync(productId);
        shown.Summary.Count.ShouldBe(1);
        shown.Summary.Average.ShouldBe(1m);
        shown.Items.Single().Body.ShouldBeNull();

        // Fixing the words sends them back for another look.
        (await SaveAsync(buyer, productId, 1, null, "The wick fell out.")).EnsureSuccessStatusCode();
        var fixedReview = (await MineAsync(buyer, productId)).Review!;
        fixedReview.ContentStatus.ShouldBe("Pending");
        fixedReview.ModerationNote.ShouldBeNull();
    }

    [DatabaseFact]
    public async Task Only_a_buyer_it_was_delivered_to_may_review_it()
    {
        var admin = await AdminClientAsync();
        var (sellerId, _) = await SellerAsync(admin);
        var (_, order) = await DeliveredAsync(admin, sellerId);
        var productId = order.Parts.Single().Lines.Single().ProductId;

        var stranger = fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile())).AccessToken);

        (await MineAsync(stranger, productId)).CanReview.ShouldBeFalse();
        (await SaveAsync(stranger, productId, 5, null, null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Anonymous callers cannot write at all.
        (await fixture.CreateClient().PutAsJsonAsync(new Uri($"/api/v1/reviews/mine/{productId}", UriKind.Relative), new { rating = 5 }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task A_buyer_who_sent_the_parcel_back_can_still_review_it()
    {
        var admin = await AdminClientAsync();
        var (sellerId, _) = await SellerAsync(admin);
        var (buyer, order) = await DeliveredAsync(admin, sellerId);
        var part = order.Parts.Single();

        (await buyer.PostAsJsonAsync(
                new Uri($"/api/v1/orders/{order.Id}/parts/{part.Id}/return", UriKind.Relative),
                new { reason = "Damaged", refundUpiId = "asha.devi@okicici" }))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync(
                new Uri($"/api/v1/admin/orders/{order.Id}/parts/{part.Id}/return-decision", UriKind.Relative),
                new { approve = true }))
            .EnsureSuccessStatusCode();
        await ProcessOutboxAsync();

        var productId = part.Lines.Single().ProductId;

        (await MineAsync(buyer, productId)).CanReview.ShouldBeTrue();
        (await SaveAsync(buyer, productId, 1, "Arrived broken", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [DatabaseFact]
    public async Task Photos_stay_private_until_approved_and_only_real_images_are_kept()
    {
        var admin = await AdminClientAsync();
        var (sellerId, _) = await SellerAsync(admin);
        var (buyer, order) = await DeliveredAsync(admin, sellerId);
        var productId = order.Parts.Single().Lines.Single().ProductId;

        // A photo needs a review to hang on.
        (await AddPhotoAsync(buyer, productId, Png, "diya.png")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await SaveAsync(buyer, productId, 5, null, null)).EnsureSuccessStatusCode();
        (await MineAsync(buyer, productId)).Review!.ContentStatus.ShouldBe("None");

        // The name says PNG; the bytes say otherwise.
        (await AddPhotoAsync(buyer, productId, "<script>alert(1)</script>"u8.ToArray(), "diya.png"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var added = await AddPhotoAsync(buyer, productId, Png, "diya.png");
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        var review = (await added.Content.ReadFromJsonAsync<ReviewDto>())!;
        review.ContentStatus.ShouldBe("Pending");

        var photo = review.Photos.Single();
        photo.Url.ShouldContain("signature=");

        var anonymous = fixture.CreateClient();
        var privateCopy = await anonymous.GetAsync(new Uri(photo.Url, UriKind.Relative));
        privateCopy.StatusCode.ShouldBe(HttpStatusCode.OK);
        privateCopy.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        privateCopy.Headers.CacheControl!.Private.ShouldBeTrue();
        (await privateCopy.Content.ReadAsByteArrayAsync()).ShouldBe(Png);

        // Without the signature, or with a forged one, a photo waiting for approval does not exist.
        var bare = new Uri($"/api/v1/reviews/photos/{photo.Id}", UriKind.Relative);
        (await anonymous.GetAsync(bare)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync(new Uri(photo.Url[..^4] + "0000", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Shoppers do not see it yet.
        (await ProductReviewsAsync(productId)).Items.Single().Photos.ShouldBeEmpty();

        (await AddPhotoAsync(buyer, productId, Png, "2.png")).EnsureSuccessStatusCode();
        (await AddPhotoAsync(buyer, productId, Png, "3.png")).EnsureSuccessStatusCode();
        (await AddPhotoAsync(buyer, productId, Png, "4.png")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await admin.PostAsync(new Uri($"/api/v1/admin/reviews/{review.Id}/approve", UriKind.Relative), null)).EnsureSuccessStatusCode();

        var shown = (await ProductReviewsAsync(productId)).Items.Single();
        shown.Photos.Count.ShouldBe(3);
        shown.Photos[0].Url.ShouldBe(bare.OriginalString);

        var publicCopy = await anonymous.GetAsync(bare);
        publicCopy.StatusCode.ShouldBe(HttpStatusCode.OK);
        publicCopy.Headers.CacheControl!.Public.ShouldBeTrue();

        // Removing one takes the file with it and leaves the approval standing.
        var before = Directory.GetFiles(fixture.PhotoFolder).Length;
        var removed = await buyer.DeleteAsync(new Uri($"/api/v1/reviews/mine/{productId}/photos/{photo.Id}", UriKind.Relative));
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = (await removed.Content.ReadFromJsonAsync<ReviewDto>())!;
        after.Photos.Count.ShouldBe(2);
        after.ContentStatus.ShouldBe("Approved");
        Directory.GetFiles(fixture.PhotoFolder).Length.ShouldBe(before - 1);
        (await anonymous.GetAsync(bare)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task The_seller_replies_and_staff_can_hide_the_reply()
    {
        var admin = await AdminClientAsync();
        var (sellerId, seller) = await SellerAsync(admin);
        var (_, otherSeller) = await SellerAsync(admin);
        var (buyer, order) = await DeliveredAsync(admin, sellerId);
        var productId = order.Parts.Single().Lines.Single().ProductId;

        var review = (await (await SaveAsync(buyer, productId, 3, "Fine", "Smaller than the picture."))
            .Content.ReadFromJsonAsync<ReviewDto>())!;

        // The seller sees the stars now and the words only once staff approve them.
        var listed = (await SellerReviewsAsync(seller)).Single(r => r.Id == review.Id);
        listed.Rating.ShouldBe(3);
        listed.Body.ShouldBeNull();

        var replyUri = new Uri($"/api/v1/seller/reviews/{review.Id}/reply", UriKind.Relative);
        (await otherSeller.PutAsJsonAsync(replyUri, new { text = "Not ours." })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await seller.PutAsJsonAsync(replyUri, new { text = "" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await seller.PutAsJsonAsync(replyUri, new { text = "Each diya is 7 cm across; we will add that to the listing." }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ProductReviewsAsync(productId)).Items.Single().Reply!.Text.ShouldStartWith("Each diya");
        (await SellerReviewsAsync(seller, unanswered: true)).ShouldNotContain(r => r.Id == review.Id);

        (await admin.PostAsync(new Uri($"/api/v1/admin/reviews/{review.Id}/reply/hide", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ProductReviewsAsync(productId)).Items.Single().Reply.ShouldBeNull();

        // Rewriting it does not bring it back.
        (await seller.PutAsJsonAsync(replyUri, new { text = "Please look again." })).EnsureSuccessStatusCode();
        (await ProductReviewsAsync(productId)).Items.Single().Reply.ShouldBeNull();
        (await SellerReviewsAsync(seller)).Single(r => r.Id == review.Id).Reply!.Hidden.ShouldBeTrue();
    }

    private static async Task<MyReviewDto> MineAsync(HttpClient buyer, Guid productId) =>
        (await buyer.GetFromJsonAsync<MyReviewDto>(new Uri($"/api/v1/reviews/mine/{productId}", UriKind.Relative)))!;

    private static Task<HttpResponseMessage> SaveAsync(HttpClient buyer, Guid productId, int rating, string? title, string? body) =>
        buyer.PutAsJsonAsync(new Uri($"/api/v1/reviews/mine/{productId}", UriKind.Relative), new { rating, title, body });

    private static Task<HttpResponseMessage> AddPhotoAsync(HttpClient buyer, Guid productId, byte[] bytes, string fileName)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var form = new MultipartFormDataContent { { file, "file", fileName } };

        return buyer.PostAsync(new Uri($"/api/v1/reviews/mine/{productId}/photos", UriKind.Relative), form);
    }

    private async Task<ProductReviewsDto> ProductReviewsAsync(Guid productId) =>
        (await fixture.CreateClient().GetFromJsonAsync<ProductReviewsDto>(
            new Uri($"/api/v1/reviews/products/{productId}", UriKind.Relative)))!;

    private static async Task<IReadOnlyList<ReviewDto>> SellerReviewsAsync(HttpClient seller, bool unanswered = false) =>
        (await seller.GetFromJsonAsync<PagedList<ReviewDto>>(
            new Uri($"/api/v1/seller/reviews?pageSize=100&unanswered={unanswered}", UriKind.Relative)))!.Items;

    /// <summary>One of a product, cash on delivery, delivered to a new buyer named Asha Devi.</summary>
    private async Task<(HttpClient Buyer, OrderDto Order)> DeliveredAsync(HttpClient admin, Guid sellerId)
    {
        (await admin.PutAsJsonAsync(new Uri("/api/v1/admin/shipping/pickup-locations", UriKind.Relative), new { sellerId, name = "Review Gaushala" }))
            .EnsureSuccessStatusCode();

        var product = await CreateProductAsync(admin, sellerId);
        var buyer = fixture.CreateAuthenticatedClient((await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile(), "Asha Devi")).AccessToken);

        (await buyer.PutAsJsonAsync(new Uri($"/api/v1/cart/items/{product.Id}", UriKind.Relative), new { quantity = 1 }))
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
        await ProcessOutboxAsync();

        return (buyer, (await buyer.GetFromJsonAsync<OrderDto>(new Uri($"/api/v1/orders/{order.Id}", UriKind.Relative)))!);
    }

    /// <summary>An approved seller with an owner account signed in.</summary>
    private async Task<(Guid Id, HttpClient Client)> SellerAsync(HttpClient admin)
    {
        var id = Guid.NewGuid();

        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

            dbContext.Add(Seller.Seed(
                id,
                new SellerApplication(
                    "Review Gaushala", null, "9000000000", null, "5 Dairy Lane", null, "Lucknow", "Uttar Pradesh",
                    "226001", "Review Gaushala", null, "AAAAA0000A", "Review Gaushala", "112233445566", "SBIN0001234"),
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
            sku = $"REV-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
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
