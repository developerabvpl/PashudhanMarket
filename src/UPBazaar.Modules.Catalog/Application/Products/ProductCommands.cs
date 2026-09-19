using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Application.Categories;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Application.Products;

/// <summary>Rules shared by create and update.</summary>
internal static class ProductRules
{
    public const decimal MaxPrice = 10_000_000m;

    public static void Details<T>(
        AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, string>> name,
        System.Linq.Expressions.Expression<Func<T, string?>> brand,
        System.Linq.Expressions.Expression<Func<T, string?>> description,
        System.Linq.Expressions.Expression<Func<T, decimal>> price,
        System.Linq.Expressions.Expression<Func<T, Guid>> categoryId)
    {
        validator.RuleFor(name).NotEmpty().MaximumLength(256);
        validator.RuleFor(brand).MaximumLength(128);
        validator.RuleFor(description).MaximumLength(4000);

        // Paise are the smallest unit anyone can pay in; a third decimal place is a typo.
        validator.RuleFor(price).GreaterThan(0).LessThan(MaxPrice)
            .Must(p => decimal.Round(p, 2) == p)
            .WithMessage("Price must be in rupees and paise, with at most two decimal places.");

        validator.RuleFor(categoryId).NotEmpty();
    }
}

/// <summary>Creates a draft listing.</summary>
public sealed record CreateProductCommand(
    string Sku,
    string Name,
    string? Brand,
    string? Description,
    decimal Price,
    Guid SellerId,
    Guid CategoryId,
    int OnHandQuantity) : ICommand<ProductDto>;

internal sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64)
            .Matches("^[A-Za-z0-9][A-Za-z0-9-]*$")
            .WithMessage("SKU may contain letters, digits and hyphens only.");
        RuleFor(x => x.SellerId).NotEmpty();
        RuleFor(x => x.OnHandQuantity).GreaterThanOrEqualTo(0);

        ProductRules.Details(this, x => x.Name, x => x.Brand, x => x.Description, x => x.Price, x => x.CategoryId);
    }
}

/// <summary>
/// Creates a listing in Draft. Publishing is a separate step so a seller can get the listing
/// right before a shopper sees it.
/// </summary>
internal sealed class CreateProductCommandHandler(UPBazaarDbContext dbContext)
    : ICommandHandler<CreateProductCommand, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        var sku = command.Sku.Trim().ToUpperInvariant();

        if (await dbContext.Set<Product>().AnyAsync(p => p.Sku == sku, cancellationToken))
        {
            return Result.Failure<ProductDto>(CatalogErrors.SkuTaken);
        }

        var category = await CategoryLoader.FindAsync(dbContext, command.CategoryId, cancellationToken);

        if (category is null)
        {
            return Result.Failure<ProductDto>(CatalogErrors.CategoryNotFound);
        }

        var product = Product.CreateDraft(
            sku,
            command.Name.Trim(),
            Normalize(command.Brand),
            Normalize(command.Description),
            command.Price,
            command.SellerId,
            category,
            command.OnHandQuantity);

        dbContext.Set<Product>().Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);

        return product.ToDto();
    }

    internal static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Changes a listing's description, price and category.</summary>
public sealed record UpdateProductCommand(
    Guid ProductId,
    string Name,
    string? Brand,
    string? Description,
    decimal Price,
    Guid CategoryId) : ICommand<ProductDto>;

internal sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();

        ProductRules.Details(this, x => x.Name, x => x.Brand, x => x.Description, x => x.Price, x => x.CategoryId);
    }
}

internal sealed class UpdateProductCommandHandler(UPBazaarDbContext dbContext)
    : ICommandHandler<UpdateProductCommand, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(
        UpdateProductCommand command,
        CancellationToken cancellationToken)
    {
        var product = await ProductLoader.Query(dbContext)
            .FirstOrDefaultAsync(p => p.PublicId == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure<ProductDto>(CatalogErrors.ProductNotFound);
        }

        var category = await CategoryLoader.FindAsync(dbContext, command.CategoryId, cancellationToken);

        if (category is null)
        {
            return Result.Failure<ProductDto>(CatalogErrors.CategoryNotFound);
        }

        var result = product.UpdateDetails(
            command.Name.Trim(),
            CreateProductCommandHandler.Normalize(command.Brand),
            CreateProductCommandHandler.Normalize(command.Description),
            command.Price,
            category);

        if (result.IsFailure)
        {
            return Result.Failure<ProductDto>(result.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return product.ToDto();
    }
}

/// <summary>Makes a draft visible to shoppers.</summary>
public sealed record PublishProductCommand(Guid ProductId) : ICommand<ProductDto>;

internal sealed class PublishProductCommandHandler(UPBazaarDbContext dbContext)
    : ICommandHandler<PublishProductCommand, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(
        PublishProductCommand command,
        CancellationToken cancellationToken)
    {
        var product = await ProductLoader.Query(dbContext)
            .FirstOrDefaultAsync(p => p.PublicId == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure<ProductDto>(CatalogErrors.ProductNotFound);
        }

        var result = product.Publish();

        if (result.IsFailure)
        {
            return Result.Failure<ProductDto>(result.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return product.ToDto();
    }
}

/// <summary>Withdraws a listing for good.</summary>
public sealed record ArchiveProductCommand(Guid ProductId) : ICommand;

/// <summary>
/// Archives rather than deletes: orders and reviews reference products, and deleting the row
/// would either break those references or quietly rewrite what a buyer bought.
/// </summary>
internal sealed class ArchiveProductCommandHandler(UPBazaarDbContext dbContext)
    : ICommandHandler<ArchiveProductCommand>
{
    public async Task<Result> HandleAsync(ArchiveProductCommand command, CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>()
            .FirstOrDefaultAsync(p => p.PublicId == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure(CatalogErrors.ProductNotFound);
        }

        product.Archive();
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Sets the physical stock count.</summary>
public sealed record SetProductStockCommand(Guid ProductId, int OnHandQuantity) : ICommand<ProductDto>;

internal sealed class SetProductStockCommandValidator : AbstractValidator<SetProductStockCommand>
{
    public SetProductStockCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.OnHandQuantity).InclusiveBetween(0, 1_000_000);
    }
}

internal sealed class SetProductStockCommandHandler(UPBazaarDbContext dbContext)
    : ICommandHandler<SetProductStockCommand, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(
        SetProductStockCommand command,
        CancellationToken cancellationToken)
    {
        var product = await ProductLoader.Query(dbContext)
            .FirstOrDefaultAsync(p => p.PublicId == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure<ProductDto>(CatalogErrors.ProductNotFound);
        }

        var result = product.SetStock(command.OnHandQuantity);

        if (result.IsFailure)
        {
            return Result.Failure<ProductDto>(result.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return product.ToDto();
    }
}
