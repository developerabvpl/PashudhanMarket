using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.Modules.Sellers.Domain;
using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Modules.Sellers.Services;

/// <summary>Implements <see cref="ISellerDirectory"/>.</summary>
internal sealed class SellerDirectory(UPBazaarDbContext dbContext) : ISellerDirectory
{
    public async Task<Guid?> GetApprovedSellerIdAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var id = await dbContext.Set<Seller>()
            .AsNoTracking()
            .Where(s => s.OwnerUserId == ownerUserId && s.Status == SellerStatus.Approved)
            .Select(s => (Guid?)s.PublicId)
            .FirstOrDefaultAsync(cancellationToken);

        return id;
    }
}

/// <summary>Sellers to create at start-up, bound from <c>Sellers</c>.</summary>
public sealed class SellersModuleOptions
{
    public const string SectionName = "Sellers";

    /// <summary>
    /// Sellers that must exist by id: in development, the one the sample catalogue was imported
    /// under. Empty in production.
    /// </summary>
    public IList<SeedSeller> SeedSellers { get; } = [];
}

/// <summary>A seller to create, approved and without an owner, if it does not exist.</summary>
public sealed class SeedSeller
{
    public Guid Id { get; set; }

    public string ShopName { get; set; } = string.Empty;
}

/// <summary>Creates the configured seed sellers that do not exist yet.</summary>
public sealed partial class SellersSeeder(
    UPBazaarDbContext dbContext,
    IOptions<SellersModuleOptions> options,
    IClock clock,
    ILogger<SellersSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var seed in options.Value.SeedSellers)
        {
            if (seed.Id == Guid.Empty || await dbContext.Set<Seller>().AnyAsync(s => s.PublicId == seed.Id, cancellationToken))
            {
                continue;
            }

            // Placeholders where an applicant would give real details; staff complete them when
            // an owner is linked. The seed exists so the sample catalogue has a real seller.
            dbContext.Set<Seller>().Add(Seller.Seed(
                seed.Id,
                new SellerApplication(
                    seed.ShopName,
                    Description: null,
                    ContactMobile: "9000000000",
                    ContactEmail: null,
                    AddressLine1: "To be completed",
                    AddressLine2: null,
                    City: "Lucknow",
                    State: "Uttar Pradesh",
                    Pincode: "226001",
                    LegalName: seed.ShopName,
                    Gstin: null,
                    Pan: "AAAAA0000A",
                    BankAccountHolder: seed.ShopName,
                    BankAccountNumber: "000000000",
                    Ifsc: "SBIN0000000"),
                clock.UtcNow));

            LogSeeded(logger, seed.ShopName, seed.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded seller {ShopName} ({SellerId})")]
    private static partial void LogSeeded(ILogger logger, string shopName, Guid sellerId);
}
