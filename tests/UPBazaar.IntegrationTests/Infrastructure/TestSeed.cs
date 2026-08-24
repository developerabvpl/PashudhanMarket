using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Messaging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Domain;

namespace UPBazaar.IntegrationTests.Infrastructure;

public static class TestSeed
{
    /// <summary>Creates a category directly, since the catalog has no category endpoint yet.</summary>
    public static async Task<Guid> CreateCategoryAsync(this ApiFixture fixture, string name)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var slug = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}";
        var category = Category.Create(name, slug);

        dbContext.Set<Category>().Add(category);
        await dbContext.SaveChangesAsync();

        return category.PublicId;
    }

    /// <summary>
    /// Drains the outbox in-line. In production Hangfire does this on a schedule; the tests
    /// run it explicitly so cross-module effects are observable at a known point.
    /// </summary>
    public static async Task DrainOutboxAsync(this ApiFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();

        await processor.ProcessAsync(CancellationToken.None);
    }

    /// <summary>Counts outbox rows of one event type mentioning the given id, processed or not.</summary>
    public static async Task<int> CountOutboxMessagesAsync(
        this ApiFixture fixture,
        string eventTypeFragment,
        string payloadFragment)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        return await dbContext.OutboxMessages.CountAsync(m =>
            m.Type.Contains(eventTypeFragment) && m.Payload.Contains(payloadFragment));
    }
}
