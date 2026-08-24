using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog;
using UPBazaar.Modules.Ordering;
using UPBazaar.Modules.Payments;
using UPBazaar.Modules.Shipping;
using UPBazaar.SharedKernel.Modules;

namespace UPBazaar.Api.Persistence;

/// <summary>
/// Used by "dotnet ef" only. It lists the modules explicitly so migrations never depend on
/// booting the web host, and so a missing connection string cannot break tooling.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<UPBazaarDbContext>
{
    private const string FallbackConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=UPBazaar;Trusted_Connection=True;TrustServerCertificate=True";

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

        IModuleSchema[] modules =
        [
            new CatalogModuleSchema(),
            new OrderingModuleSchema(),
            new PaymentsModuleSchema(),
            new ShippingModuleSchema(),
        ];

        return new UPBazaarDbContext(options, modules);
    }
}
