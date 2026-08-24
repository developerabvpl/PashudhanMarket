using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using UPBazaar.Infrastructure.Persistence;

namespace UPBazaar.IntegrationTests.Infrastructure;

/// <summary>
/// One SQL Server and one API host shared by every integration test, migrated once from empty.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string WebhookSecret = "test-webhook-secret";

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

    /// <summary>An HTTP client whose principal carries exactly the permissions given.</summary>
    public HttpClient CreateClientWith(params string[] permissions)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader, string.Join(',', permissions));

        return client;
    }

    /// <summary>Signs a webhook body the way the gateway would, for the signature header.</summary>
    public static string SignWebhook(string payload) => WebhookSignature.Compute(payload, WebhookSecret);

    public IServiceScope CreateScope() => Services.CreateScope();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting feeds host configuration, which Program.cs can already see while it is
        // still registering services. ConfigureAppConfiguration would land too late for that.
        builder.UseSetting("ConnectionStrings:UPBazaar", _connectionString);

        // Hangfire would need its own storage connection; the tests drive the outbox directly.
        builder.UseSetting("Hangfire:Enabled", "false");
        builder.UseSetting("Hangfire:EnableServer", "false");

        builder.UseSetting("ExternalServices:UseSandbox", "true");
        builder.UseSetting("ExternalServices:Razorpay:WebhookSecret", WebhookSecret);

        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-integration-test-signing-key");
        builder.UseSetting("Jwt:Issuer", "https://upbazaar.test");
        builder.UseSetting("Jwt:Audience", "upbazaar-api");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // The fallback policy still applies; only the scheme is swapped.
            services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName)
                    .RequireAuthenticatedUser()
                    .Build());
        });
    }

    private async Task<string> StartContainerAsync()
    {
        // Pinned image: the parameterless builder is obsolete, and a floating tag would make
        // the suite depend on whatever SQL Server version was pulled last.
        _container = new MsSqlBuilder(MsSqlImage).Build();
        await _container.StartAsync();

        return _container.GetConnectionString();
    }

    private async Task<string> CreateDatabaseOnAsync(string serverConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(serverConnectionString)
        {
            TrustServerCertificate = true,
        };

        _createdDatabase = $"UPBazaar_Tests_{Guid.NewGuid():N}";
        builder.InitialCatalog = "master";

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

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
