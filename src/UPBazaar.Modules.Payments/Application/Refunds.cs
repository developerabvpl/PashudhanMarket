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
/// Records the refund owed out of an order's payment when one of its parts will not reach the
/// buyer - cancelled, or sent back undelivered. Orders works out the amount; this finds the
/// payment it comes out of. The outbox may deliver an event more than once, and the payment
/// ignores a part it has already recorded. An exception is left to propagate so the outbox
/// retries, because a refund that silently fails to be recorded is money nobody returns.
/// </summary>
internal sealed partial class PartRefundRecorder(
    UPBazaarDbContext dbContext,
    IClock clock,
    ILogger<PartRefundRecorder> logger)
{
    public async Task RecordAsync(
        Guid orderId,
        string orderNumber,
        Guid partId,
        decimal refundDue,
        string reason,
        CancellationToken cancellationToken)
    {
        if (refundDue <= 0)
        {
            return;
        }

        var payment = await dbContext.Set<Payment>()
            .Include(p => p.Refunds)
            .Where(p => p.OrderId == orderId && p.Status == PaymentStatus.Paid)
            .OrderByDescending(p => p.PaidAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            LogNoPayment(logger, orderNumber, refundDue);

            return;
        }

        var recorded = payment.RecordPartRefundDue(partId, refundDue, reason, clock.UtcNow);

        if (recorded.IsFailure)
        {
            LogRefundRefused(logger, orderNumber, recorded.Error.Code);

            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Order {Number} owes a refund of {Amount} but has no paid payment")]
    private static partial void LogNoPayment(ILogger logger, string number, decimal amount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Refund for order {Number} was not recorded: {ErrorCode}")]
    private static partial void LogRefundRefused(ILogger logger, string number, string errorCode);
}

/// <summary>A part of a paid order was cancelled: its share is owed back.</summary>
internal sealed class OrderPartCancelledHandler(PartRefundRecorder refunds)
    : IDomainEventHandler<OrderPartCancelledDomainEvent>
{
    public Task HandleAsync(OrderPartCancelledDomainEvent e, CancellationToken cancellationToken) =>
        refunds.RecordAsync(e.OrderId, e.Number, e.PartId, e.RefundDue, "Part of the order was cancelled.", cancellationToken);
}

/// <summary>
/// A part of a paid order came back undelivered and is with the seller again. Recorded only now,
/// not when the courier first turned round, because an RTO can still be reversed on the way.
/// </summary>
internal sealed class OrderPartReturnedHandler(PartRefundRecorder refunds)
    : IDomainEventHandler<OrderPartReturnedDomainEvent>
{
    public Task HandleAsync(OrderPartReturnedDomainEvent e, CancellationToken cancellationToken) =>
        refunds.RecordAsync(
            e.OrderId, e.Number, e.PartId, e.RefundDue, "The parcel could not be delivered and went back to the seller.", cancellationToken);
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
