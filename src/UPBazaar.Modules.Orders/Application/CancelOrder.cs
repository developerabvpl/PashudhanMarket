using FluentValidation;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Application;

/// <summary>Cancels a whole order.</summary>
/// <param name="OrderId">Public id.</param>
/// <param name="BuyerId">Set when the buyer is cancelling their own; null for staff and the system.</param>
/// <param name="Reason">Why. Shown to the buyer.</param>
public sealed record CancelOrderCommand(Guid OrderId, Guid? BuyerId, string Reason) : ICommand<OrderDto>;

internal sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

internal sealed class CancelOrderCommandHandler(OrderCanceller canceller)
    : ICommandHandler<CancelOrderCommand, OrderDto>
{
    public Task<Result<OrderDto>> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken) =>
        canceller.CancelAsync(command.OrderId, command.BuyerId, command.Reason, cancellationToken);
}

/// <summary>
/// Cancels an order and gives its stock back, in one transaction. Shared by the buyer's cancel,
/// staff's cancel and the job that cancels unpaid orders, so all three leave stock the same way.
/// </summary>
internal sealed class OrderCanceller(
    UPBazaarDbContext dbContext,
    OrderTransaction transaction,
    OrderReader reader,
    OrderStock stock,
    IClock clock)
{
    public Task<Result<OrderDto>> CancelAsync(
        Guid orderId,
        Guid? buyerId,
        string reason,
        CancellationToken cancellationToken) =>
        transaction.RunAsync(async ct =>
        {
            var order = buyerId is { } buyer
                ? await reader.FindForBuyerAsync(orderId, buyer, ct)
                : await reader.FindAsync(orderId, ct);

            if (order is null)
            {
                return Result.Failure<OrderDto>(OrderErrors.NotFound);
            }

            var wasAwaitingPayment = order.Status == OrderStatus.PendingPayment;
            var cancelled = order.Cancel(reason.Trim(), clock.UtcNow);

            if (cancelled.IsFailure)
            {
                return Result.Failure<OrderDto>(cancelled.Error);
            }

            var returned = await stock.GiveBackAsync(order, wasAwaitingPayment, cancelled.Value, ct);

            if (returned.IsFailure)
            {
                return Result.Failure<OrderDto>(returned.Error);
            }

            if (transaction.WasDetached(order))
            {
                return Result.Failure<OrderDto>(OrderErrors.ConcurrentChange);
            }

            await dbContext.SaveChangesAsync(ct);

            return order.ToDto();
        }, cancellationToken);
}
