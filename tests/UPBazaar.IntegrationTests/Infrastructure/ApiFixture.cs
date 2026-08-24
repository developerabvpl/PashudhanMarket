using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using UPBazaar.Infrastructure.Persistence;

namespace UPBazaar.IntegrationTests.Infrastructure;

/// <summary>
/// One SQL Server and one API host, shared by every integration test and migrated once from
/// an empty database. Migrating rather than calling EnsureCreated is deliberate: it means
/// these tests also prove the migration applies to a fresh database.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Pinned so the suite does not depend on whichever tag was pulled last.</summary>
    private const string MsSqlImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private MsSqlContainer? _container;
    private string _connectionString = string.Empty;
    private string? _createdDatabase;

    public async Task InitializeAsync()
    {
        if (!TestDatabase.IsAvailable)
        {
            return;
        }

        _connectionString = TestDatabase.ConfiguredConnectionString is { } configured
            ? await CreateDatabaseOnAsync(configured)
            : await StartContainerAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UPBazaarDbContext>();

        await dbContext.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
        else if (_createdDatabase is not null && TestDatabase.ConfiguredConnectionString is { } configured)
        {
            await DropDatabaseAsync(configured, _createdDatabase);
        }
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");

        // Development validates the container; Testing would not. Turning it on here is what
        // makes a captive dependency - a singleton holding a scoped service - fail in CI
        // instead of on the first `dotnet run`.
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });

        // UseSetting feeds host configuration, which Program.cs can already read while it is
        // still registering services; ConfigureAppConfiguration would land too late for that.
        builder.UseSetting("ConnectionStrings:UPBazaar", _connectionString);

        // Hangfire would want its own schema and a background server; the tests drive the
        // outbox directly instead.
        builder.UseSetting("Hangfire:Enabled", "false");
        builder.UseSetting("Hangfire:EnableServer", "false");

        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-integration-test-signing-key");
        builder.UseSetting("Jwt:Issuer", "https://upbazaar.test");
        builder.UseSetting("Jwt:Audience", "upbazaar-api");
    }

    private async Task<string> StartContainerAsync()
    {
        _container = new MsSqlBuilder(MsSqlImage).Build();
        await _container.StartAsync();

        return _container.GetConnectionString();
    }

    /// <summary>
    /// Creates a uniquely named database on an existing server, so parallel runs and repeated
    /// runs never collide.
    /// </summary>
    private async Task<string> CreateDatabaseOnAsync(string serverConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(serverConnectionString)
        {
            TrustServerCertificate = true,
            InitialCatalog = "master",
        };

        _createdDatabase = $"UPBazaar_Tests_{Guid.NewGuid():N}";

        await using (var connection = new SqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{_createdDatabase}]";
            await command.ExecuteNonQueryAsync();
        }

        builder.InitialCatalog = _createdDatabase;

        return builder.ConnectionString;
    }

    private static async Task DropDatabaseAsync(string serverConnectionString, string database)
    {
        var builder = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = "master",
            TrustServerCertificate = true,
        };

        // Pooled connections would keep the database in use and block the drop.
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID('{database}') IS NOT NULL BEGIN "
            + $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
            + $"DROP DATABASE [{database}]; END";

        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>Shares one host and database across every integration test class.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
