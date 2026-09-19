using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UPBazaar.Api.Configuration;
using Testcontainers.MsSql;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Notifications.Contracts;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.IntegrationTests.Infrastructure;

/// <summary>
/// One SQL Server and one API host, shared by every integration test and migrated once from an
/// empty database. Migrating rather than calling EnsureCreated is deliberate: it means these
/// tests also prove the migrations apply to a fresh database.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Pinned so the suite does not depend on whichever tag was pulled last.</summary>
    private const string MsSqlImage = "mcr.microsoft.com/mssql/server:2022-latest";

    /// <summary>Seeded administrator, used by tests that need full permissions.</summary>
    public const string SuperAdminEmail = "superadmin@upbazaar.test";

    /// <summary>Password for <see cref="SuperAdminEmail"/>.</summary>
    public const string SuperAdminPassword = "seed-super-admin-password";

    private MsSqlContainer? _container;
    private string _connectionString = string.Empty;
    private string? _createdDatabase;

    /// <summary>Intercepts SMS so tests can read the one-time code that was issued.</summary>
    public CapturingSmsSender Sms { get; } = new();

    /// <summary>Intercepts email so tests can read the password-reset token.</summary>
    public CapturingEmailSender Email { get; } = new();

    public async Task InitializeAsync()
    {
        if (!TestDatabase.IsAvailable)
        {
            return;
        }

        _connectionString = TestDatabase.ConfiguredConnectionString is { } configured
            ? await CreateDatabaseOnAsync(configured)
            : await StartContainerAsync();

        // Migrated through a standalone context rather than the host's, because touching
        // Services starts the host, and startup seeding needs the schema to already exist.
        await MigrateAsync();

        // Touching Services now boots the host, which seeds permissions, roles and the
        // SuperAdmin configured below.
        _ = Services;
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

    /// <summary>A client carrying a bearer token.</summary>
    public HttpClient CreateAuthenticatedClient(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

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

        builder.UseSetting("Identity:SuperAdmin:Email", SuperAdminEmail);
        builder.UseSetting("Identity:SuperAdmin:Password", SuperAdminPassword);
        builder.UseSetting("Identity:SuperAdmin:DisplayName", "Seeded Super Admin");

        // Tests build their own catalogue; the sample import would make counts depend on a file.
        builder.UseSetting("Catalog:SeedFile", string.Empty);

        builder.ConfigureTestServices(services =>
        {
            // Swap delivery for capture at the same boundary a carrier would occupy.
            services.RemoveAll<ISmsSender>();
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<ISmsSender>(Sms);
            services.AddSingleton<IEmailSender>(Email);
        });
    }

    private async Task MigrateAsync()
    {
        var options = new DbContextOptionsBuilder<UPBazaarDbContext>()
            .UseSqlServer(_connectionString, sql =>
            {
                // Must match the host exactly. With the default history table this writes to
                // dbo.__EFMigrationsHistory while the application reads shared, and every
                // migration then looks pending on startup.
                sql.MigrationsAssembly(typeof(UPBazaarDbContext).Assembly.GetName().Name);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", UPBazaarDbContext.SharedSchema);
            })
            .Options;

        // The full module set, not an empty one: before migrating, EF compares the current
        // model against the migration snapshot, and a context missing the module entities
        // would look like a pile of pending changes.
        await using var dbContext = new UPBazaarDbContext(options, ResolveModules());

        await dbContext.Database.MigrateAsync();
    }

    /// <summary>
    /// Runs the application's own module registration against a throwaway container and reads
    /// the modules back, so the migration model is by construction the one the host composes.
    /// </summary>
    private static IEnumerable<IModule> ResolveModules()
    {
        var services = new ServiceCollection();
        services.AddModules(new ConfigurationBuilder().Build(), new MigrationHostEnvironment());

        using var provider = services.BuildServiceProvider();

        return [.. provider.GetServices<IModule>()];
    }

    /// <summary>Minimal environment for module registration during migration.</summary>
    private sealed class MigrationHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Migration";

        public string ApplicationName { get; set; } = "UPBazaar.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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
