using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.ExternalServices.Payments;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Payments.Contracts;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Services;

/// <summary>
/// Opens a gateway order for a freshly placed order. Re-running with the same idempotency key
/// returns the existing payment, because checkout itself may be retried.
/// </summary>
internal sealed class PaymentInitiationService(
    UPBazaarDbContext dbContext,
    IPaymentGateway gateway) : IPaymentInitiation
{
    public async Task<Result<PaymentInitiationDto>> CreateForOrderAsync(
        CreatePaymentForOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Amount <= 0)
        {
            return Result.Failure<PaymentInitiationDto>(PaymentErrors.InvalidAmount);
        }

        var existing = await dbContext.Set<Payment>()
            .FirstOrDefaultAsync(p => p.OrderId == request.OrderId, cancellationToken);

        if (existing is not null)
        {
            return new PaymentInitiationDto(
                existing.PublicId,
                existing.GatewayOrderId,
                existing.Provider,
                existing.Amount,
                existing.Currency);
        }

        var gatewayOrder = await gateway.CreateOrderAsync(
            new GatewayOrderRequest(
                request.IdempotencyKey,
                request.Amount,
                request.Currency,
                request.OrderNumber),
            cancellationToken);

        var payment = Payment.Open(
            request.OrderId,
            request.OrderNumber,
            request.SellerId,
            gateway.Provider,
            gatewayOrder.GatewayOrderId,
            request.Amount,
            request.Currency);

        dbContext.Set<Payment>().Add(payment);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PaymentInitiationDto(
            payment.PublicId,
            payment.GatewayOrderId,
            payment.Provider,
            payment.Amount,
            payment.Currency);
    }
}
