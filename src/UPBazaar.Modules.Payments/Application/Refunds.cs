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
        RefundReason reason,
        CancellationToken cancellationToken)
    {
        if (refundDue <= 0)
        {
            return;
        }

        var payment = await dbContext.Set<Payment>()
            .Include(p => p.Refunds)
            .Where(p => p.OrderId == orderId && p.Status == PaymentStatus.Paid && p.OrderOutcome != OrderOutcome.Refused)
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

    /// <summary>
    /// Records a refund of cash paid at the door, owed to the UPI id the buyer gave. There is no
    /// payment to hang it on, so it stands alone; a part already refunded is left as it is.
    /// </summary>
    public async Task RecordUpiAsync(
        Guid orderId,
        string orderNumber,
        Guid partId,
        decimal refundDue,
        string currency,
        string upiId,
        RefundReason reason,
        CancellationToken cancellationToken)
    {
        if (refundDue <= 0 || await dbContext.Set<Refund>().AnyAsync(r => r.OrderPartId == partId, cancellationToken))
        {
            return;
        }

        dbContext.Set<Refund>().Add(Refund.ToUpi(orderId, orderNumber, partId, refundDue, currency, upiId, reason, clock.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Order {Number} owes a refund of {Amount} but has no paid payment")]
    private static partial void LogNoPayment(ILogger logger, string number, decimal amount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Refund for order {Number} was not recorded: {ErrorCode}")]
    private static partial void LogRefundRefused(ILogger logger, string number, string errorCode);
}

/// <summary>
/// A part of a paid order was cancelled: its share is owed back. The reason says whether the whole
/// order went with it, because staff read it to the buyer and "part of the order" on an order with
/// nothing left in it reads like a mistake.
/// </summary>
internal sealed class OrderPartCancelledHandler(PartRefundRecorder refunds)
    : IDomainEventHandler<OrderPartCancelledDomainEvent>
{
    public Task HandleAsync(OrderPartCancelledDomainEvent e, CancellationToken cancellationToken) =>
        refunds.RecordAsync(
            e.OrderId,
            e.Number,
            e.PartId,
            e.RefundDue,
            e.OrderCancelled ? RefundReason.OrderCancelled : RefundReason.PartCancelled,
            cancellationToken);
}

/// <summary>
/// A part is back with the seller: undelivered, or returned by the buyer. Recorded only now, not
/// when it started back, because a return can still go wrong on the way. A buyer's return of a
/// cash-on-delivery order is owed to the UPI id they gave; everything else comes out of the
/// online payment.
/// </summary>
internal sealed class OrderPartReturnedHandler(PartRefundRecorder refunds)
    : IDomainEventHandler<OrderPartReturnedDomainEvent>
{
    public Task HandleAsync(OrderPartReturnedDomainEvent e, CancellationToken cancellationToken)
    {
        if (!e.RequestedByBuyer)
        {
            return refunds.RecordAsync(
                e.OrderId, e.Number, e.PartId, e.RefundDue, RefundReason.Undelivered, cancellationToken);
        }

        const RefundReason reason = RefundReason.BuyerReturn;

        return e.RefundUpiId is { } upiId
            ? refunds.RecordUpiAsync(e.OrderId, e.Number, e.PartId, e.RefundDue, e.Currency, upiId, reason, cancellationToken)
            : refunds.RecordAsync(e.OrderId, e.Number, e.PartId, e.RefundDue, reason, cancellationToken);
    }
}

/// <summary>
/// Staff record that they have refunded the money - in the Razorpay dashboard, or by UPI - with
/// the refund id Razorpay gave them or the UPI transaction reference, so the record shows where
/// the money went.
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
        var refund = await dbContext.Set<Refund>().FirstOrDefaultAsync(r => r.PublicId == command.RefundId, cancellationToken);

        if (refund is null)
        {
            return Result.Failure<RefundDto>(PaymentErrors.RefundNotFound);
        }

        var payment = refund.PaymentId is { } paymentId
            ? await dbContext.Set<Payment>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            : null;

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
