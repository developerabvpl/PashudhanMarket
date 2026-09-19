using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UPBazaar.Api.Configuration;
using UPBazaar.Infrastructure.Persistence;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Api.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> only.
///
/// It builds the context directly rather than booting the web host, so migrations do not
/// depend on Hangfire reaching SQL Server or on a connection string being present. The module
/// list comes from <see cref="ModuleRegistration"/>, so the design-time model is by
/// construction the same one the running application composes.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<UPBazaarDbContext>
{
    private const string FallbackConnectionString =
        @"Server=.;Database=UPBazaar;Trusted_Connection=True;TrustServerCertificate=True";

    public UPBazaarDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("UPBAZAAR_ConnectionStrings__UPBazaar")
            ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<UPBazaarDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(UPBazaarDbContext).Assembly.GetName().Name);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", UPBazaarDbContext.SharedSchema);
            })
            .Options;

        return new UPBazaarDbContext(options, ResolveModules());
    }

    /// <summary>
    /// Runs the real registration against a throwaway container and reads the modules back
    /// out. Keeping a second hand-written list here is how design-time models drift from the
    /// running one; this cannot drift, because it is the same code path.
    /// </summary>
    private static IEnumerable<IModule> ResolveModules()
    {
        var services = new ServiceCollection();
        services.AddModules(
            new ConfigurationBuilder().Build(),
            new DesignTimeHostEnvironment());

        using var provider = services.BuildServiceProvider();

        return [.. provider.GetServices<IModule>()];
    }
}

/// <summary>
/// Minimal environment for design-time module registration. Named "DesignTime" rather than
/// "Development" so modules that stub themselves for development do not do so here.
/// </summary>
internal sealed class DesignTimeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "DesignTime";

    public string ApplicationName { get; set; } = "UPBazaar.Api";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } =
        new NullFileProvider();
}
