using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Domain;

namespace UPBazaar.Modules.Catalog.Services;

/// <summary>Settings for the Catalog module.</summary>
public sealed class CatalogModuleOptions
{
    public const string SectionName = "Catalog";

    /// <summary>
    /// Path to a catalogue JSON file in the shape of <c>web/tools/data/catalog.json</c>, imported
    /// once into an empty catalogue. Relative paths resolve against the content root. Unset in
    /// production: a live catalogue is built through the API, not from a file.
    /// </summary>
    public string? SeedFile { get; set; }
}

/// <summary>
/// Imports the surveyed product list into an empty catalogue.
///
/// The public ids in the file are kept, so every product URL the static storefront has already
/// published keeps working when the storefront switches to this API. It runs only while the
/// catalogue is empty: once anyone has created a product, the database is the source of truth
/// and a file must never overwrite it.
/// </summary>
public sealed partial class CatalogSeeder(
    UPBazaarDbContext dbContext,
    IOptions<CatalogModuleOptions> options,
    ILogger<CatalogSeeder> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Imports the file when one is configured and the catalogue is empty.</summary>
    /// <param name="contentRoot">Base for a relative <see cref="CatalogModuleOptions.SeedFile"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SeedAsync(string contentRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Value.SeedFile))
        {
            return;
        }

        if (await dbContext.Set<Product>().AnyAsync(cancellationToken)
            || await dbContext.Set<Category>().AnyAsync(cancellationToken))
        {
            return;
        }

        var path = Path.GetFullPath(options.Value.SeedFile, contentRoot);

        if (!File.Exists(path))
        {
            // Loud but not fatal: a missing sample file should not stop a developer's API.
            LogSeedFileMissing(logger, path);
            return;
        }

        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<SeedFile>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException($"{path} is empty.");

        var categories = new Dictionary<Guid, Category>();

        // Parents first, so a child can point at a category that already exists.
        foreach (var raw in file.Categories.OrderBy(c => c.ParentId is null ? 0 : 1))
        {
            var parent = raw.ParentId is { } parentId ? categories[parentId] : null;
            var category = Category.Create(raw.Name, parent, raw.Id);

            categories[raw.Id] = category;
            dbContext.Set<Category>().Add(category);
        }

        foreach (var raw in file.Products)
        {
            if (!categories.TryGetValue(raw.CategoryId, out var category))
            {
                throw new InvalidOperationException(
                    $"Product {raw.Sku} names category {raw.CategoryId}, which is not in {path}.");
            }

            var product = Product.CreateDraft(
                raw.Sku,
                raw.Name,
                raw.Brand,
                raw.Description,
                raw.Price,
                raw.SellerId,
                category,
                raw.OnHandQuantity,
                raw.Id);

            if (string.Equals(raw.Status, nameof(ProductStatus.Active), StringComparison.OrdinalIgnoreCase))
            {
                product.Publish();
            }

            // Imported listings are not news: they were never unpublished anywhere anyone could
            // see, so nothing downstream should react as if they had just gone live.
            product.ClearDomainEvents();

            dbContext.Set<Product>().Add(product);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        LogSeeded(logger, file.Products.Count, file.Categories.Count, path);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalog seed file {Path} does not exist; skipping the import")]
    private static partial void LogSeedFileMissing(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Imported {Products} products in {Categories} categories from {Path}")]
    private static partial void LogSeeded(ILogger logger, int products, int categories, string path);

    private sealed record SeedFile(
        [property: JsonRequired] IReadOnlyList<SeedCategory> Categories,
        [property: JsonRequired] IReadOnlyList<SeedProduct> Products);

    private sealed record SeedCategory(Guid Id, string Name, Guid? ParentId);

    private sealed record SeedProduct(
        Guid Id,
        string Sku,
        string Name,
        string? Brand,
        string? Description,
        decimal Price,
        string Status,
        Guid SellerId,
        Guid CategoryId,
        int OnHandQuantity);
}
