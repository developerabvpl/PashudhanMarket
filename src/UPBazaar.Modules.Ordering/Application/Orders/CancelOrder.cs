using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Contracts;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Ordering.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Ordering.Application.Orders;

public sealed record CancelOrderCommand(Guid OrderId, string Reason) : ICommand;

internal sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(256);
    }
}

/// <summary>Cancels an unpaid order and hands its held stock straight back to the catalog.</summary>
internal sealed class CancelOrderCommandHandler(
    UPBazaarDbContext dbContext,
    IStockReservations stockReservations) : ICommandHandler<CancelOrderCommand>
{
    public async Task<Result> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await dbContext.Set<Order>()
            .FirstOrDefaultAsync(o => o.PublicId == command.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure(OrderErrors.NotFound);
        }

        var cancelled = order.Cancel(command.Reason);

        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        var released = await stockReservations.ReleaseAsync(
            order.Lines.Select(l => new StockReservationLine(l.ProductId, l.Quantity)).ToList(),
            cancellationToken);

        if (released.IsFailure)
        {
            return released;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
