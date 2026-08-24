using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Application.Stock;

/// <summary>Absolute stock-take correction, not a delta.</summary>
public sealed record AdjustStockCommand(Guid ProductId, int OnHand) : ICommand;

internal sealed class AdjustStockCommandValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.OnHand).GreaterThanOrEqualTo(0);
    }
}

internal sealed class AdjustStockCommandHandler(UPBazaarDbContext dbContext)
    : ICommandHandler<AdjustStockCommand>
{
    public async Task<Result> HandleAsync(AdjustStockCommand command, CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>()
            .FirstOrDefaultAsync(p => p.PublicId == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure(CatalogErrors.ProductNotFound);
        }

        var adjusted = product.Stock.AdjustOnHand(command.OnHand);

        if (adjusted.IsFailure)
        {
            return adjusted;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(CatalogErrors.ConcurrencyConflict);
        }

        return Result.Success();
    }
}
