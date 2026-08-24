using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Domain;

namespace UPBazaar.Modules.Payments.Application;

internal static class PaymentMappings
{
    public static PaymentDto ToDto(this Payment payment) => new(
        payment.PublicId,
        payment.OrderId,
        payment.GatewayOrderId,
        payment.Provider,
        payment.Amount,
        payment.Currency,
        payment.Status.ToString(),
        payment.CreatedAtUtc,
        payment.CapturedAtUtc);

    public static RefundDto ToDto(this Refund refund, Guid paymentId) => new(
        refund.PublicId,
        paymentId,
        refund.GatewayRefundId,
        refund.Amount,
        refund.Status.ToString(),
        refund.CreatedAtUtc);

    public static SettlementLineDto ToDto(this SettlementLine line, Guid paymentId) => new(
        line.PublicId,
        paymentId,
        line.GrossAmount,
        line.CommissionAmount,
        line.NetAmount,
        line.Status.ToString(),
        line.SettledAtUtc);
}
