using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Application.Products;

public sealed record CreateProductCommand(
    Guid SellerId,
    string Sku,
    string Name,
    string? Description,
    Guid CategoryId,
    decimal Price,
    string Currency,
    int InitialStock) : ICommand<ProductDto>;

internal sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.SellerId).NotEmpty();
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Price).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.InitialStock).GreaterThanOrEqualTo(0);
    }
}

internal sealed class CreateProductCommandHandler(UPBazaarDbContext dbContext)
    : ICommandHandler<CreateProductCommand, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.Set<Category>()
            .FirstOrDefaultAsync(c => c.PublicId == command.CategoryId, cancellationToken);

        if (category is null)
        {
            return Result.Failure<ProductDto>(CatalogErrors.CategoryNotFound);
        }

        var sku = command.Sku.Trim().ToUpperInvariant();

        if (await dbContext.Set<Product>().AnyAsync(p => p.Sku == sku, cancellationToken))
        {
            return Result.Failure<ProductDto>(CatalogErrors.DuplicateSku);
        }

        // Two sellers may legitimately list "Banarasi Silk Saree"; the slug is unique, so the
        // second one gets its SKU appended rather than a 500 from the unique index.
        var slug = CatalogMappings.ToSlug(command.Name);

        if (await dbContext.Set<Product>().AnyAsync(p => p.Slug == slug, cancellationToken))
        {
            slug = $"{slug}-{sku.ToLowerInvariant()}";
        }

        var product = Product.Create(
            command.SellerId,
            sku,
            command.Name,
            slug,
            command.Description,
            category,
            command.Price,
            command.Currency,
            command.InitialStock);

        dbContext.Set<Product>().Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);

        return product.ToDto();
    }
}
