using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Contracts;

/// <summary>
/// The only way another module may open a payment. Must be safe to call twice with the same
/// IdempotencyKey, because checkout itself is retryable.
/// </summary>
public interface IPaymentInitiation
{
    Task<Result<PaymentInitiationDto>> CreateForOrderAsync(
        CreatePaymentForOrderRequest request,
        CancellationToken cancellationToken);
}
