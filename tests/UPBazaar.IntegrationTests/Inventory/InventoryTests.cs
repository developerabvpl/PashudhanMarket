using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Inventory.Contracts;
using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.Modules.Inventory.Domain;
using UPBazaar.Modules.Inventory.Services;

namespace UPBazaar.IntegrationTests.Inventory;

[Collection(ApiCollection.Name)]
public sealed class InventoryTests(ApiFixture fixture)
{
    private static readonly TimeSpan Hold = TimeSpan.FromMinutes(15);

    private readonly AuthClient _auth = new(fixture);

    [DatabaseFact]
    public async Task Opening_stock_given_at_creation_is_owned_by_inventory_and_shown_on_the_product()
    {
        var admin = await AdminClientAsync();
        var product = await CreatePublishedProductAsync(admin, openingStock: 12);

        var stock = await admin.GetFromJsonAsync<StockDetailDto>(StockUri(product.Id));

        stock!.Level.OnHandQuantity.ShouldBe(12);
        stock.RecentMovements.Single().Reason.ShouldBe("Opening stock");

        var shown = await fixture.CreateClient().GetFromJsonAsync<ProductDto>(ProductUri(product.Id));
        shown!.OnHandQuantity.ShouldBe(12);
    }

    [DatabaseFact]
    public async Task Receipts_counts_and_write_offs_move_stock_and_are_recorded_with_who_did_them()
    {
        var admin = await AdminClientAsync();
        var product = await CreatePublishedProductAsync(admin, openingStock: 5);

        (await admin.PostAsJsonAsync(StockUri(product.Id, "receipts"), new { quantity = 10, reference = "DN-42" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync(StockUri(product.Id, "write-offs"), new { quantity = 3, reason = "Wet in storage" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync(StockUri(product.Id), new { onHandQuantity = 11, reason = "Shelf count" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var stock = await admin.GetFromJsonAsync<StockDetailDto>(StockUri(product.Id));

        stock!.Level.OnHandQuantity.ShouldBe(11);
        stock.RecentMovements.Select(m => m.Type).ShouldBe(["Counted", "WrittenOff", "Received", "Received"]);
        stock.RecentMovements.Take(3).ShouldAllBe(m => m.RecordedBy != null);
    }

    [DatabaseFact]
    public async Task A_write_off_needs_a_reason()
    {
        var admin = await AdminClientAsync();
        var product = await CreatePublishedProductAsync(admin, openingStock: 5);

        (await admin.PostAsJsonAsync(StockUri(product.Id, "write-offs"), new { quantity = 1 }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [DatabaseFact]
    public async Task Stock_cannot_be_recorded_for_a_product_that_does_not_exist()
    {
        var admin = await AdminClientAsync();

        (await admin.PostAsJsonAsync(StockUri(Guid.NewGuid(), "receipts"), new { quantity = 1 }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task A_buyer_cannot_touch_stock()
    {
        var buyer = await _auth.SignInBuyerByOtpAsync(AuthClient.NewMobile());
        var client = fixture.CreateAuthenticatedClient(buyer.AccessToken);

        (await client.GetAsync(new Uri("/api/v1/admin/inventory/stock", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [DatabaseFact]
    public async Task A_reservation_holds_stock_off_sale_and_commit_removes_it()
    {
        var admin = await AdminClientAsync();
        var product = await CreatePublishedProductAsync(admin, openingStock: 10);

        var reservation = await InScopeAsync(s => s.ReserveAsync("order-1", [new(product.Id, 4)], Hold, default));
        reservation.IsSuccess.ShouldBeTrue();

        (await AvailableAsync(product.Id)).ShouldBe(6);

        (await InScopeAsync(s => s.CommitAsync(reservation.Value, default))).IsSuccess.ShouldBeTrue();

        var level = await LevelAsync(product.Id);
        level.OnHandQuantity.ShouldBe(6);
        level.ReservedQuantity.ShouldBe(0);
    }

    [DatabaseFact]
    public async Task A_reservation_is_all_or_nothing()
    {
        var admin = await AdminClientAsync();
        var plenty = await CreatePublishedProductAsync(admin, openingStock: 10);
        var scarce = await CreatePublishedProductAsync(admin, openingStock: 1);

        var result = await InScopeAsync(s =>
            s.ReserveAsync("order-2", [new(plenty.Id, 2), new(scarce.Id, 2)], Hold, default));

        result.Error.ShouldBe(InventoryErrors.InsufficientStock);
        (await AvailableAsync(plenty.Id)).ShouldBe(10);
    }

    [DatabaseFact]
    public async Task A_released_reservation_cannot_then_be_committed()
    {
        var admin = await AdminClientAsync();
        var product = await CreatePublishedProductAsync(admin, openingStock: 3);

        var reservation = await InScopeAsync(s => s.ReserveAsync("order-3", [new(product.Id, 3)], Hold, default));
        await InScopeAsync(s => s.ReleaseAsync(reservation.Value, default));

        (await AvailableAsync(product.Id)).ShouldBe(3);
        (await InScopeAsync(s => s.CommitAsync(reservation.Value, default))).Error
            .ShouldBe(InventoryErrors.ReservationNotActive);
    }

    [DatabaseFact]
    public async Task An_abandoned_reservation_expires_and_its_stock_goes_back_on_sale()
    {
        var admin = await AdminClientAsync();
        var product = await CreatePublishedProductAsync(admin, openingStock: 5);

        var reservation = await InScopeAsync(s =>
            s.ReserveAsync("order-4", [new(product.Id, 5)], TimeSpan.FromMilliseconds(1), default));

        await Task.Delay(50);

        // Committing after the hold lapsed is refused even before the job has swept it up.
        (await InScopeAsync(s => s.CommitAsync(reservation.Value, default))).Error
            .ShouldBe(InventoryErrors.ReservationNotActive);

        using (var scope = fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReservationExpiryJob>().ExpireAsync(default);
        }

        (await AvailableAsync(product.Id)).ShouldBe(5);
    }

    [DatabaseFact]
    public async Task Two_checkouts_racing_for_the_last_units_never_oversell()
    {
        var admin = await AdminClientAsync();
        var product = await CreatePublishedProductAsync(admin, openingStock: 1);

        // Each in its own scope, so each has its own DbContext: two requests, not one.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            InScopeAsync(s => s.ReserveAsync($"race-{i}", [new(product.Id, 1)], Hold, default))));

        attempts.Count(r => r.IsSuccess).ShouldBe(1);

        var level = await LevelAsync(product.Id);
        level.ReservedQuantity.ShouldBe(1);
        level.AvailableQuantity.ShouldBe(0);
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await _auth.SignInAsSuperAdminAsync();

        return fixture.CreateAuthenticatedClient(tokens.AccessToken);
    }

    private static async Task<ProductDto> CreatePublishedProductAsync(HttpClient admin, int openingStock)
    {
        var category = await (await admin.PostAsJsonAsync(
                new Uri("/api/v1/admin/catalog/categories", UriKind.Relative),
                new { name = $"Category {Guid.NewGuid():N}" }))
            .Content.ReadFromJsonAsync<CategoryDto>();

        var response = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/catalog/products", UriKind.Relative), new
        {
            sku = $"INV-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            name = "Gobar Kande, pack of 10",
            price = 99m,
            sellerId = Guid.NewGuid(),
            categoryId = category!.Id,
            onHandQuantity = openingStock,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var product = (await response.Content.ReadFromJsonAsync<ProductDto>())!;

        await admin.PostAsync(new Uri($"/api/v1/admin/catalog/products/{product.Id}/publish", UriKind.Relative), null);

        return product;
    }

    private async Task<T> InScopeAsync<T>(Func<IInventoryService, Task<T>> call)
    {
        using var scope = fixture.CreateScope();

        return await call(scope.ServiceProvider.GetRequiredService<IInventoryService>());
    }

    private async Task<StockLevelDto> LevelAsync(Guid productId)
    {
        var levels = await InScopeAsync(s => s.GetStockLevelsAsync([productId], default));

        return levels[productId];
    }

    private async Task<int> AvailableAsync(Guid productId) => (await LevelAsync(productId)).AvailableQuantity;

    private static Uri StockUri(Guid productId, string? action = null) =>
        new($"/api/v1/admin/inventory/stock/{productId}{(action is null ? string.Empty : "/" + action)}", UriKind.Relative);

    private static Uri ProductUri(Guid productId) =>
        new($"/api/v1/catalog/products/{productId}", UriKind.Relative);
}
