using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Catalog.Domain;
using UPBazaar.Modules.Inventory.Contracts;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Application.Products;

/// <summary>
/// A seller puts one of their products on sale: a lower price between two moments. The seller
/// bears it - they are paid on the sale price - so only the seller sets one. Setting a sale
/// replaces any sale already set.
/// </summary>
/// <param name="SellerId">The seller; always the caller's own shop.</param>
/// <param name="ProductId">Their product.</param>
/// <param name="SalePrice">The price while it runs, below the regular price.</param>
/// <param name="StartsAtUtc">When it starts; now when null.</param>
/// <param name="EndsAtUtc">When the regular price comes back.</param>
public sealed record SetProductSaleCommand(Guid SellerId, Guid ProductId, decimal SalePrice, DateTime? StartsAtUtc, DateTime EndsAtUtc)
    : ICommand<ProductDto>;

internal sealed class SetProductSaleCommandValidator : AbstractValidator<SetProductSaleCommand>
{
    public SetProductSaleCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.SalePrice).GreaterThan(0).PrecisionScale(10, 2, ignoreTrailingZeros: true);
    }
}

internal sealed class SetProductSaleCommandHandler(UPBazaarDbContext dbContext, IInventoryService inventory, IClock clock)
    : ICommandHandler<SetProductSaleCommand, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(SetProductSaleCommand command, CancellationToken cancellationToken)
    {
        var product = await SellerProductAccess.FindOwnedAsync(dbContext, command.SellerId, command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure<ProductDto>(CatalogErrors.ProductNotFound);
        }

        var now = clock.UtcNow;
        var set = product.SetSale(command.SalePrice, command.StartsAtUtc ?? now, command.EndsAtUtc, now);

        if (set.IsFailure)
        {
            return Result.Failure<ProductDto>(set.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await product.ToDtoAsync(inventory, now, cancellationToken);
    }
}

/// <summary>
/// Ends a product's sale now, or calls off one still to come: by its seller, or by staff, who can
/// end any sale - say one that misleads.
/// </summary>
/// <param name="ProductId">The product.</param>
/// <param name="SellerId">The seller ending their own sale; null for staff.</param>
public sealed record EndProductSaleCommand(Guid ProductId, Guid? SellerId) : ICommand<ProductDto>;

internal sealed class EndProductSaleCommandHandler(UPBazaarDbContext dbContext, IInventoryService inventory, IClock clock)
    : ICommandHandler<EndProductSaleCommand, ProductDto>
{
    public async Task<Result<ProductDto>> HandleAsync(EndProductSaleCommand command, CancellationToken cancellationToken)
    {
        var product = command.SellerId is { } seller
            ? await SellerProductAccess.FindOwnedAsync(dbContext, seller, command.ProductId, cancellationToken)
            : await ProductLoader.Query(dbContext).FirstOrDefaultAsync(p => p.PublicId == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure<ProductDto>(CatalogErrors.ProductNotFound);
        }

        product.EndSale();

        await dbContext.SaveChangesAsync(cancellationToken);

        return await product.ToDtoAsync(inventory, clock.UtcNow, cancellationToken);
    }
}
