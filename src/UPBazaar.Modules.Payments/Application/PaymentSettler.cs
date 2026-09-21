using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Application;

/// <summary>
/// Takes a captured payment the rest of the way: records the money, then hands it to the order.
///
/// The one path every report of a payment goes through - the buyer's browser, Razorpay's webhook,
/// and the settlement job that catches whatever those two missed - so all three agree, and any of
/// them arriving twice is harmless.
///
/// The two steps are saved separately on purpose. Orders runs its own transaction and clears the
/// shared change tracker when it does, so the payment is saved as Paid first and read again after.
/// If the process dies in between, the payment is Paid with its outcome Pending, which is exactly
/// what the settlement job looks for.
/// </summary>
internal sealed class PaymentSettler(
    UPBazaarDbContext dbContext,
    IOrderPaymentService orders,
    IClock clock)
{
    /// <summary>Orders' "try again" answer, as opposed to a refusal.</summary>
    private const string OrdersConcurrentChange = "orders.concurrent_change";

    /// <summary>
    /// Records the money against the payment for this gateway order, then applies it to the order.
    /// With a buyer id, the payment must be that buyer's.
    /// </summary>
    public async Task<Result<PaymentResultDto>> SettleAsync(
        string gatewayOrderId,
        string gatewayPaymentId,
        Guid? buyerId,
        CancellationToken cancellationToken)
    {
        var recorded = await RecordPaidAsync(gatewayOrderId, gatewayPaymentId, buyerId, cancellationToken);

        return recorded.IsFailure
            ? Result.Failure<PaymentResultDto>(recorded.Error)
            : await ApplyToOrderAsync(recorded.Value, cancellationToken);
    }

    /// <summary>
    /// Tells Orders about a Paid payment whose outcome is still Pending. Orders either confirms the
    /// order, refuses for good - and then everything is owed back - or is busy, in which case the
    /// payment stays Pending for the next attempt.
    /// </summary>
    public async Task<Result<PaymentResultDto>> ApplyToOrderAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();

        var payment = await FindAsync(paymentId, cancellationToken);

        if (payment is null)
        {
            return Result.Failure<PaymentResultDto>(PaymentErrors.NotFound);
        }

        if (payment.Status != PaymentStatus.Paid || payment.OrderOutcome != OrderOutcome.Pending)
        {
            return Outcome(payment);
        }

        var confirmed = await orders.ConfirmPaymentAsync(
            payment.OrderId, payment.Amount, payment.GatewayPaymentId!, cancellationToken);

        // Orders cleared the tracker; work from a fresh copy.
        dbContext.ChangeTracker.Clear();
        payment = (await FindAsync(paymentId, cancellationToken))!;

        if (confirmed.IsSuccess)
        {
            payment.RecordOrderConfirmed();
        }
        else if (confirmed.Error.Code != OrdersConcurrentChange)
        {
            payment.RecordOrderRefused(confirmed.Error.Message, clock.UtcNow);
        }
        else
        {
            return Outcome(payment);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The browser and the webhook settled the same payment at once; the other one won.
            dbContext.ChangeTracker.Clear();
            payment = (await FindAsync(paymentId, cancellationToken))!;
        }

        return Outcome(payment);
    }

    private async Task<Result<Guid>> RecordPaidAsync(
        string gatewayOrderId,
        string gatewayPaymentId,
        Guid? buyerId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            dbContext.ChangeTracker.Clear();

            var payment = await dbContext.Set<Payment>()
                .FirstOrDefaultAsync(p => p.GatewayOrderId == gatewayOrderId, cancellationToken);

            if (payment is null || (buyerId is { } buyer && payment.BuyerId != buyer))
            {
                return Result.Failure<Guid>(PaymentErrors.NotFound);
            }

            var paid = payment.MarkPaid(gatewayPaymentId, clock.UtcNow);

            if (paid.IsFailure)
            {
                return Result.Failure<Guid>(paid.Error);
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                return payment.PublicId;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                // The same payment reported by another request a moment ago; marking it again is
                // a no-op once that one's save is visible.
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure<Guid>(PaymentErrors.ConcurrentChange);
            }
        }
    }

    private Task<Payment?> FindAsync(Guid paymentId, CancellationToken cancellationToken) =>
        dbContext.Set<Payment>()
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.PublicId == paymentId, cancellationToken);

    private static PaymentResultDto Outcome(Payment payment) => new(
        payment.OrderId,
        payment.OrderOutcome switch
        {
            OrderOutcome.Confirmed => "Confirmed",
            OrderOutcome.Refused => "RefundDue",
            _ => "Processing",
        });
}
