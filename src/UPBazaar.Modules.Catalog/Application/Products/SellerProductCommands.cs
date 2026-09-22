using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Application.Categories;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;
using UPBazaar.Modules.Inventory.Contracts;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Application.Products;

/// <summary>
/// A seller's own listing, or nothing. Another seller's product answers exactly like one that
/// does not exist, so product ids cannot be probed from the seller portal.
/// </summary>
internal static class SellerProductAccess
{
    public static Task<Product?> FindOwnedAsync(
        UPBazaarDbContext dbContext,
        Guid sellerId,
        Guid productId,
        CancellationToken cancellationToken) =>
        ProductLoader.Query(dbContext)
            .FirstOrDefaultAsync(p => p.PublicId == productId && p.SellerId == sellerId, cancellationToken);
}

/// <summary>One of the seller's own listings, in any status.</summary>
public sealed record GetSellerProductQuery(Guid SellerId, Guid ProductId) : IQuery<ProductDto>;

internal sealed class GetSellerProductQueryHandler(UPBazaarDbContext dbContext, IInventoryService inventory)
    : IQueryHandler<GetSellerProductQuery, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(GetSellerProductQuery query, CancellationToken cancellationToken)
    {
        var product = await SellerProductAccess.FindOwnedAsync(dbContext, query.SellerId, query.ProductId, cancellationToken);

        return product is null
            ? Result.Failure<ProductDto>(CatalogErrors.ProductNotFound)
            : await product.ToDtoAsync(inventory, cancellationToken);
    }
}

/// <summary>
/// A seller edits their draft. Only a draft: what a live listing claims about itself was checked
/// by a moderator, so changing it means asking one. Price, stock and parcel size stay editable on
/// a live listing through their own commands.
/// </summary>
public sealed record UpdateSellerDraftCommand(
    Guid SellerId,
    Guid ProductId,
    string Name,
    string? Brand,
    string? Description,
    decimal Price,
    Guid CategoryId) : ICommand<ProductDto>;

internal sealed class UpdateSellerDraftCommandValidator : AbstractValidator<UpdateSellerDraftCommand>
{
    public UpdateSellerDraftCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();

        ProductRules.Details(this, x => x.Name, x => x.Brand, x => x.Description, x => x.Price, x => x.CategoryId);
    }
}

internal sealed class UpdateSellerDraftCommandHandler(UPBazaarDbContext dbContext, IInventoryService inventory)
    : ICommandHandler<UpdateSellerDraftCommand, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(UpdateSellerDraftCommand command, CancellationToken cancellationToken)
    {
        var product = await SellerProductAccess.FindOwnedAsync(dbContext, command.SellerId, command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure<ProductDto>(CatalogErrors.ProductNotFound);
        }

        if (product.Status != ProductStatus.Draft)
        {
            return Result.Failure<ProductDto>(CatalogErrors.NotADraft);
        }

        var category = await CategoryLoader.FindAsync(dbContext, command.CategoryId, cancellationToken);

        if (category is null)
        {
            return Result.Failure<ProductDto>(CatalogErrors.CategoryNotFound);
        }

        var updated = product.UpdateDetails(command.Name, command.Brand, command.Description, command.Price, category);

        if (updated.IsFailure)
        {
            return Result.Failure<ProductDto>(updated.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await product.ToDtoAsync(inventory, cancellationToken);
    }
}

/// <summary>A seller changes the price of one of their listings, draft or live.</summary>
public sealed record RepriceSellerProductCommand(Guid SellerId, Guid ProductId, decimal Price) : ICommand<ProductDto>;

internal sealed class RepriceSellerProductCommandValidator : AbstractValidator<RepriceSellerProductCommand>
{
    public RepriceSellerProductCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Price).GreaterThan(0).LessThanOrEqualTo(ProductRules.MaxPrice);
    }
}

internal sealed class RepriceSellerProductCommandHandler(UPBazaarDbContext dbContext, IInventoryService inventory)
    : ICommandHandler<RepriceSellerProductCommand, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(RepriceSellerProductCommand command, CancellationToken cancellationToken)
    {
        var product = await SellerProductAccess.FindOwnedAsync(dbContext, command.SellerId, command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure<ProductDto>(CatalogErrors.ProductNotFound);
        }

        var repriced = product.Reprice(command.Price);

        if (repriced.IsFailure)
        {
            return Result.Failure<ProductDto>(repriced.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await product.ToDtoAsync(inventory, cancellationToken);
    }
}

/// <summary>A seller asks for their draft to be published.</summary>
public sealed record SubmitProductForReviewCommand(Guid SellerId, Guid ProductId) : ICommand<ProductDto>;

internal sealed class SubmitProductForReviewCommandHandler(UPBazaarDbContext dbContext, IInventoryService inventory)
    : ICommandHandler<SubmitProductForReviewCommand, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(SubmitProductForReviewCommand command, CancellationToken cancellationToken)
    {
        var product = await SellerProductAccess.FindOwnedAsync(dbContext, command.SellerId, command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure<ProductDto>(CatalogErrors.ProductNotFound);
        }

        var submitted = product.SubmitForReview();

        if (submitted.IsFailure)
        {
            return Result.Failure<ProductDto>(submitted.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await product.ToDtoAsync(inventory, cancellationToken);
    }
}

/// <summary>A moderator sends a listing in review back to its seller, saying what to fix.</summary>
public sealed record SendProductBackCommand(Guid ProductId, string Note) : ICommand<ProductDto>;

internal sealed class SendProductBackCommandValidator : AbstractValidator<SendProductBackCommand>
{
    public SendProductBackCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Note).NotEmpty().MaximumLength(1000);
    }
}

internal sealed class SendProductBackCommandHandler(UPBazaarDbContext dbContext, IInventoryService inventory)
    : ICommandHandler<SendProductBackCommand, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(SendProductBackCommand command, CancellationToken cancellationToken)
    {
        var product = await ProductLoader.Query(dbContext).FirstOrDefaultAsync(p => p.PublicId == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure<ProductDto>(CatalogErrors.ProductNotFound);
        }

        var sent = product.SendBack(command.Note);

        if (sent.IsFailure)
        {
            return Result.Failure<ProductDto>(sent.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await product.ToDtoAsync(inventory, cancellationToken);
    }
}
