using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Infrastructure.Persistence.Shared;
using UPBazaar.IntegrationTests.Infrastructure;
using UPBazaar.SharedKernel.Outbox;

namespace UPBazaar.IntegrationTests;

/// <summary>
/// Checks the shape the Baseline migration produced. The fixture migrated an empty database,
/// so these assertions are what "the migration applies to a fresh database" means in practice.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BaselineMigrationTests(ApiFixture fixture)
{
    [DatabaseFact]
    public async Task Baseline_is_the_applied_migration_and_nothing_is_pending()
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var applied = await dbContext.Database.GetAppliedMigrationsAsync();
        var pending = await dbContext.Database.GetPendingMigrationsAsync();

        applied.ShouldContain(m => m.EndsWith("Baseline", StringComparison.Ordinal));
        pending.ShouldBeEmpty();
    }

    [DatabaseTheory]
    [InlineData("shared", "AuditLog")]
    [InlineData("shared", "OutboxMessages")]
    [InlineData("shared", "AppSettings")]
    [InlineData("shared", "FeatureFlags")]
    public async Task The_baseline_tables_exist(string schema, string table)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var exists = await TableExistsAsync(dbContext, schema, table);

        exists.ShouldBeTrue($"{schema}.{table} should have been created by the Baseline migration.");
    }

    [DatabaseFact]
    public async Task Only_modules_with_entities_own_tables()
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var schemas = await QueryStringsAsync(
            dbContext,
            """
            SELECT DISTINCT TABLE_SCHEMA
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA <> 'dbo'
            """);

        // Every module declares a schema, but a schema only materialises once the module has
        // an entity. Identity, Catalog, Inventory, Cart and Orders do; the other eleven are still
        // skeletons. This assertion is the tripwire for a module accidentally creating tables
        // outside its own schema.
        schemas.Order(StringComparer.Ordinal).ShouldBe(["cart", "catalog", "identity", "inventory", "orders", "shared"]);
    }

    [DatabaseFact]
    public async Task Shared_tables_round_trip()
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var key = $"tests.roundtrip.{Guid.NewGuid():N}";

        dbContext.AppSettings.Add(new AppSetting
        {
            Key = key,
            Value = "42",
            Module = "shared",
            UpdatedAtUtc = DateTime.UtcNow,
        });

        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            EventId = Guid.CreateVersion7(),
            Type = "UPBazaar.Tests.Probe, UPBazaar.IntegrationTests",
            Payload = """{"probe":true}""",
            Module = "shared",
            OccurredAtUtc = DateTime.UtcNow,
        });

        await dbContext.SaveChangesAsync();

        var stored = await dbContext.AppSettings.AsNoTracking().SingleAsync(s => s.Key == key);
        stored.Value.ShouldBe("42");
    }

    [DatabaseFact]
    public async Task A_feature_flag_rollout_outside_zero_to_one_hundred_is_rejected_by_the_database()
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        dbContext.FeatureFlags.Add(new FeatureFlag
        {
            Key = $"tests.flag.{Guid.NewGuid():N}",
            Module = "shared",
            IsEnabled = true,
            RolloutPercentage = 150,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        // The check constraint is the last line of defence when code forgets to validate.
        await Should.ThrowAsync<DbUpdateException>(async () => await dbContext.SaveChangesAsync());
    }

    [DatabaseFact]
    public async Task An_outbox_event_id_cannot_be_enqueued_twice()
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        var eventId = Guid.CreateVersion7();

        for (var i = 0; i < 2; i++)
        {
            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                EventId = eventId,
                Type = "UPBazaar.Tests.Duplicate, UPBazaar.IntegrationTests",
                Payload = "{}",
                OccurredAtUtc = DateTime.UtcNow,
            });
        }

        await Should.ThrowAsync<DbUpdateException>(async () => await dbContext.SaveChangesAsync());
    }

    private static async Task<bool> TableExistsAsync(DbContext dbContext, string schema, string table)
    {
        var found = await QueryStringsAsync(
            dbContext,
            $"""
             SELECT TABLE_NAME
             FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA = '{schema}' AND TABLE_NAME = '{table}'
             """);

        return found.Count == 1;
    }

    private static async Task<List<string>> QueryStringsAsync(DbContext dbContext, string sql)
    {
        var results = new List<string>();

        var connection = dbContext.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;

        if (wasClosed)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(reader.GetString(0));
            }
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }

        return results;
    }
}
