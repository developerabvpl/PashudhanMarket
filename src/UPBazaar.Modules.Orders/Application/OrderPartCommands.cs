using FluentValidation;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Application;

/// <summary>
/// Moves one seller's part forward: Packed, Shipped or Delivered.
///
/// Staff do this by hand for now. Once Shipping is wired to Shiprocket, courier status updates
/// will drive the same transitions.
/// </summary>
public sealed record AdvanceOrderPartCommand(Guid OrderId, Guid PartId, string Status) : ICommand<OrderDto>;

internal sealed class AdvanceOrderPartCommandValidator : AbstractValidator<AdvanceOrderPartCommand>
{
    public AdvanceOrderPartCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.PartId).NotEmpty();
        RuleFor(x => x.Status)
            .Must(s => Enum.TryParse<OrderPartStatus>(s, ignoreCase: true, out var status)
                && status is OrderPartStatus.Packed or OrderPartStatus.Shipped or OrderPartStatus.Delivered)
            .WithMessage("Status must be Packed, Shipped or Delivered.");
    }
}

internal sealed class AdvanceOrderPartCommandHandler(
    UPBazaarDbContext dbContext,
    OrderTransaction transaction,
    OrderReader reader) : ICommandHandler<AdvanceOrderPartCommand, OrderDto>
{
    public Task<Result<OrderDto>> HandleAsync(AdvanceOrderPartCommand command, CancellationToken cancellationToken) =>
        transaction.RunAsync(async ct =>
        {
            var order = await reader.FindAsync(command.OrderId, ct);

            if (order is null)
            {
                return Result.Failure<OrderDto>(OrderErrors.NotFound);
            }

            var moved = order.AdvancePart(
                command.PartId,
                Enum.Parse<OrderPartStatus>(command.Status, ignoreCase: true));

            if (moved.IsFailure)
            {
                return Result.Failure<OrderDto>(moved.Error);
            }

            await dbContext.SaveChangesAsync(ct);

            return order.ToDto();
        }, cancellationToken);
}

/// <summary>Cancels one seller's part of a confirmed order and puts its stock back.</summary>
public sealed record CancelOrderPartCommand(Guid OrderId, Guid PartId, string Reason) : ICommand<OrderDto>;

internal sealed class CancelOrderPartCommandValidator : AbstractValidator<CancelOrderPartCommand>
{
    public CancelOrderPartCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.PartId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

internal sealed class CancelOrderPartCommandHandler(
    UPBazaarDbContext dbContext,
    OrderTransaction transaction,
    OrderReader reader,
    OrderStock stock,
    IClock clock) : ICommandHandler<CancelOrderPartCommand, OrderDto>
{
    public Task<Result<OrderDto>> HandleAsync(CancelOrderPartCommand command, CancellationToken cancellationToken) =>
        transaction.RunAsync(async ct =>
        {
            var order = await reader.FindAsync(command.OrderId, ct);

            if (order is null)
            {
                return Result.Failure<OrderDto>(OrderErrors.NotFound);
            }

            var cancelled = order.CancelPart(command.PartId, command.Reason.Trim(), clock.UtcNow);

            if (cancelled.IsFailure)
            {
                return Result.Failure<OrderDto>(cancelled.Error);
            }

            var part = order.Parts.Single(p => p.PublicId == command.PartId);
            var returned = await stock.GiveBackAsync(order, wasAwaitingPayment: false, [part], ct);

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
