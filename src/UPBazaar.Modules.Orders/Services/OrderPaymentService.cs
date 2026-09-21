using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Inventory.Contracts;
using UPBazaar.Modules.Orders.Application;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Services;

/// <summary>Implements <see cref="IOrderPaymentService"/> for Payments.</summary>
internal sealed class OrderPaymentService(
    UPBazaarDbContext dbContext,
    OrderTransaction transaction,
    OrderReader reader,
    IInventoryService inventory,
    IClock clock) : IOrderPaymentService
{
    public async Task<Result<PayableOrderDto>> GetPayableAsync(
        Guid orderId,
        Guid buyerId,
        CancellationToken cancellationToken)
    {
        var order = await reader.FindReadOnlyAsync(orderId, buyerId, cancellationToken);

        if (order is null)
        {
            return Result.Failure<PayableOrderDto>(OrderErrors.NotFound);
        }

        if (order.Status != OrderStatus.PendingPayment || order.PaymentDueAtUtc is not { } due)
        {
            return Result.Failure<PayableOrderDto>(OrderErrors.NotAwaitingPayment);
        }

        if (clock.UtcNow > due)
        {
            return Result.Failure<PayableOrderDto>(OrderErrors.PaymentTooLate);
        }

        return new PayableOrderDto(order.PublicId, order.Number, order.BuyerId, order.Total, order.Currency, due);
    }

    /// <summary>
    /// Confirms the order first, then commits its stock, then saves both together. The order is
    /// checked first so that a wrong amount or a lapsed deadline never touches stock; the commit
    /// can still fail if Inventory's hold ran out, and then the whole thing rolls back.
    /// </summary>
    public async Task<Result> ConfirmPaymentAsync(
        Guid orderId,
        decimal amount,
        string paymentReference,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentReference);

        var result = await transaction.RunAsync(async ct =>
        {
            var order = await reader.FindAsync(orderId, ct);

            if (order is null)
            {
                return Result.Failure<bool>(OrderErrors.NotFound);
            }

            var alreadyPaid = order.PaymentStatus == PaymentStatus.Paid;
            var confirmed = order.ConfirmPayment(amount, paymentReference, clock.UtcNow);

            if (confirmed.IsFailure)
            {
                return Result.Failure<bool>(confirmed.Error);
            }

            if (alreadyPaid)
            {
                // The same payment reported twice; the first report did all the work.
                return Result.Success(true);
            }

            var committed = await inventory.CommitAsync(order.ReservationId, ct);

            if (committed.IsFailure)
            {
                return Result.Failure<bool>(OrderErrors.PaymentTooLate);
            }

            if (transaction.WasDetached(order))
            {
                return Result.Failure<bool>(OrderErrors.ConcurrentChange);
            }

            await dbContext.SaveChangesAsync(ct);

            return Result.Success(true);
        }, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }
}
