using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Application;

/// <summary>
/// Records the refund owed when part of a paid order is cancelled.
///
/// Orders works out the amount - the part's subtotal if the order was paid online, else zero - and
/// this finds the payment it comes out of. The outbox may deliver the event more than once; the
/// payment ignores a part it has already recorded. An exception here is left to propagate so the
/// outbox retries, because a refund that silently fails to be recorded is money nobody returns.
/// </summary>
internal sealed partial class OrderPartCancelledHandler(
    UPBazaarDbContext dbContext,
    IClock clock,
    ILogger<OrderPartCancelledHandler> logger) : IDomainEventHandler<OrderPartCancelledDomainEvent>
{
    public async Task HandleAsync(OrderPartCancelledDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.RefundDue <= 0)
        {
            return;
        }

        var payment = await dbContext.Set<Payment>()
            .Include(p => p.Refunds)
            .Where(p => p.OrderId == domainEvent.OrderId && p.Status == PaymentStatus.Paid)
            .OrderByDescending(p => p.PaidAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            LogNoPayment(logger, domainEvent.Number, domainEvent.RefundDue);

            return;
        }

        var recorded = payment.RecordPartRefundDue(
            domainEvent.PartId,
            domainEvent.RefundDue,
            "Part of the order was cancelled.",
            clock.UtcNow);

        if (recorded.IsFailure)
        {
            LogRefundRefused(logger, domainEvent.Number, recorded.Error.Code);

            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Order {Number} owes a refund of {Amount} but has no paid payment")]
    private static partial void LogNoPayment(ILogger logger, string number, decimal amount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Refund for order {Number} was not recorded: {ErrorCode}")]
    private static partial void LogRefundRefused(ILogger logger, string number, string errorCode);
}

/// <summary>
/// Staff record that they have refunded the money in the Razorpay dashboard, with the refund id
/// Razorpay gave them, so the record shows where the money went.
/// </summary>
public sealed record MarkRefundedCommand(Guid RefundId, string GatewayRefundId) : ICommand<RefundDto>;

internal sealed class MarkRefundedCommandValidator : AbstractValidator<MarkRefundedCommand>
{
    public MarkRefundedCommandValidator()
    {
        RuleFor(x => x.RefundId).NotEmpty();
        RuleFor(x => x.GatewayRefundId).NotEmpty().MaximumLength(64);
    }
}

internal sealed class MarkRefundedCommandHandler(UPBazaarDbContext dbContext, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<MarkRefundedCommand, RefundDto>
{
    public async Task<Result<RefundDto>> HandleAsync(MarkRefundedCommand command, CancellationToken cancellationToken)
    {
        var payment = await dbContext.Set<Payment>()
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.Refunds.Any(r => r.PublicId == command.RefundId), cancellationToken);

        var refund = payment?.Refunds.Single(r => r.PublicId == command.RefundId);

        if (payment is null || refund is null)
        {
            return Result.Failure<RefundDto>(PaymentErrors.RefundNotFound);
        }

        var marked = refund.MarkRefunded(command.GatewayRefundId, currentUser.UserId, clock.UtcNow);

        if (marked.IsFailure)
        {
            return Result.Failure<RefundDto>(marked.Error);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<RefundDto>(PaymentErrors.ConcurrentChange);
        }

        return refund.ToDto(payment);
    }
}
