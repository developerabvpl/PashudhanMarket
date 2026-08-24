using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Application.Products;

public sealed record PublishProductCommand(Guid ProductId) : ICommand;

internal sealed class PublishProductCommandValidator : AbstractValidator<PublishProductCommand>
{
    public PublishProductCommandValidator() => RuleFor(x => x.ProductId).NotEmpty();
}

internal sealed class PublishProductCommandHandler(UPBazaarDbContext dbContext)
    : ICommandHandler<PublishProductCommand>
{
    public async Task<Result> HandleAsync(
        PublishProductCommand command,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Set<Product>()
            .FirstOrDefaultAsync(p => p.PublicId == command.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure(CatalogErrors.ProductNotFound);
        }

        var published = product.Publish();

        if (published.IsFailure)
        {
            return published;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
