using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Identity.Services;

namespace UPBazaar.Api.Configuration;

/// <summary>Startup seeding of reference data.</summary>
public static class SeedSetup
{
    /// <summary>
    /// Reconciles permissions and roles with the catalogue, and creates the configured
    /// SuperAdmin if it is missing.
    ///
    /// Runs on every boot and is idempotent. If the database has not been migrated it fails
    /// loudly rather than starting an app whose authorization tables are empty — an API that
    /// accepts requests but can authorise nobody is worse than one that refuses to start.
    /// </summary>
    /// <param name="app">Web application.</param>
    /// <returns>A task that completes when seeding is done.</returns>
    public static async Task SeedIdentityAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");

        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();
        var pending = await dbContext.Database.GetPendingMigrationsAsync();

        if (pending.Any())
        {
            logger.LogError(
                "The database has {Count} pending migration(s). Run: dotnet ef database update "
                + "-p src/UPBazaar.Infrastructure -s src/UPBazaar.Api",
                pending.Count());

            throw new InvalidOperationException(
                "Cannot seed identity data: the database schema is out of date.");
        }

        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
    }
}
