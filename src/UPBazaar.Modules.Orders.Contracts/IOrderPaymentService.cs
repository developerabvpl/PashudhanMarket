using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Contracts;

/// <summary>
/// What Payments needs from Orders to take an online payment.
///
/// Orders knows nothing about the payment provider. Payments asks what is owed, takes the money
/// however the provider does it, and reports back; Orders then commits the stock and confirms.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>
    /// What is due on one of this buyer's unpaid online orders. Not found for anybody else's
    /// order; a conflict once it is paid, cancelled or past its payment deadline.
    /// </summary>
    Task<Result<PayableOrderDto>> GetPayableAsync(Guid orderId, Guid buyerId, CancellationToken cancellationToken);

    /// <summary>
    /// Records that the order has been paid, commits its stock and confirms it. Confirming an
    /// order already paid under the same reference succeeds again, so a repeated webhook is harmless.
    /// </summary>
    /// <param name="orderId">Public id.</param>
    /// <param name="amount">What was actually taken; must equal the amount due.</param>
    /// <param name="paymentReference">The provider's payment id, kept on the order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A conflict when the order was cancelled or its stock hold lapsed first. The money has been
    /// taken regardless, so on failure the caller must refund it.
    /// </returns>
    Task<Result> ConfirmPaymentAsync(
        Guid orderId,
        decimal amount,
        string paymentReference,
        CancellationToken cancellationToken);
}
