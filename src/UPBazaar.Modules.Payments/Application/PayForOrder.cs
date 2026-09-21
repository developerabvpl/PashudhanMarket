using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.Modules.Payments.Gateway;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Application;

/// <summary>Opens a payment for one of the buyer's unpaid online orders.</summary>
public sealed record StartPaymentCommand(Guid BuyerId, Guid OrderId) : ICommand<CheckoutSessionDto>;

/// <summary>
/// Asks Orders what is owed, creates the Razorpay order to pay it against, and returns what
/// Checkout needs.
///
/// Pressing "Pay now" twice, or coming back after closing the Checkout window, reuses the open
/// payment rather than creating another Razorpay order: one order, one gateway order, so there is
/// never a second thing the buyer could pay for the same goods.
/// </summary>
internal sealed class StartPaymentCommandHandler(
    UPBazaarDbContext dbContext,
    IOrderPaymentService orders,
    IPaymentGateway gateway) : ICommandHandler<StartPaymentCommand, CheckoutSessionDto>
{
    private const string SupportedCurrency = "INR";

    public async Task<Result<CheckoutSessionDto>> HandleAsync(
        StartPaymentCommand command,
        CancellationToken cancellationToken)
    {
        if (!gateway.IsEnabled)
        {
            return Result.Failure<CheckoutSessionDto>(PaymentErrors.OnlineDisabled);
        }

        var payable = await orders.GetPayableAsync(command.OrderId, command.BuyerId, cancellationToken);

        if (payable.IsFailure)
        {
            return Result.Failure<CheckoutSessionDto>(payable.Error);
        }

        var due = payable.Value;

        if (due.Currency != SupportedCurrency)
        {
            return Result.Failure<CheckoutSessionDto>(PaymentErrors.UnsupportedCurrency);
        }

        var existing = await dbContext.Set<Payment>()
            .Where(p => p.OrderId == due.OrderId)
            .ToListAsync(cancellationToken);

        // Money already taken and on its way to the order: a second payment would charge twice.
        if (existing.Any(p => p.Status == PaymentStatus.Paid))
        {
            return Result.Failure<CheckoutSessionDto>(PaymentErrors.OrderAlreadyPaid);
        }

        var payment = existing.FirstOrDefault(p =>
            p.Status == PaymentStatus.Created && p.Gateway == gateway.Name && p.Amount == due.Amount);

        if (payment is null)
        {
            var gatewayOrder = await gateway.CreateOrderAsync(
                ToPaise(due.Amount), due.Currency, due.Number, due.OrderId, cancellationToken);

            if (gatewayOrder.IsFailure)
            {
                return Result.Failure<CheckoutSessionDto>(gatewayOrder.Error);
            }

            payment = Payment.Create(
                due.OrderId,
                due.Number,
                due.BuyerId,
                due.Amount,
                due.Currency,
                gateway.Name,
                gatewayOrder.Value,
                due.PaymentDueAtUtc);

            dbContext.Set<Payment>().Add(payment);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new CheckoutSessionDto(
            payment.PublicId,
            gateway.Name,
            gateway.KeyId,
            payment.GatewayOrderId,
            ToPaise(payment.Amount),
            payment.Currency,
            payment.OrderNumber,
            due.PaymentDueAtUtc);
    }

    /// <summary>Razorpay counts in paise. Rupee amounts carry two decimals, so this is exact.</summary>
    internal static long ToPaise(decimal amount) => (long)decimal.Round(amount * 100m, MidpointRounding.AwayFromZero);
}

/// <summary>What Razorpay Checkout hands the browser after a successful payment.</summary>
public sealed record VerifyPaymentCommand(
    Guid BuyerId,
    string GatewayOrderId,
    string GatewayPaymentId,
    string Signature) : ICommand<PaymentResultDto>;

internal sealed class VerifyPaymentCommandValidator : AbstractValidator<VerifyPaymentCommand>
{
    public VerifyPaymentCommandValidator()
    {
        RuleFor(x => x.GatewayOrderId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.GatewayPaymentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Signature).NotEmpty().MaximumLength(128);
    }
}

/// <summary>
/// Checks the browser's report of a payment and, if the signature holds, settles it. The
/// signature is the whole of the trust here: it proves Razorpay, not the buyer, produced the ids.
/// </summary>
internal sealed class VerifyPaymentCommandHandler(IPaymentGateway gateway, PaymentSettler settler)
    : ICommandHandler<VerifyPaymentCommand, PaymentResultDto>
{
    public async Task<Result<PaymentResultDto>> HandleAsync(
        VerifyPaymentCommand command,
        CancellationToken cancellationToken)
    {
        if (!gateway.IsPaymentSignatureValid(command.GatewayOrderId, command.GatewayPaymentId, command.Signature))
        {
            return Result.Failure<PaymentResultDto>(PaymentErrors.InvalidSignature);
        }

        return await settler.SettleAsync(
            command.GatewayOrderId, command.GatewayPaymentId, command.BuyerId, cancellationToken);
    }
}

/// <summary>
/// Development only: pays a fake gateway order as if the buyer had completed Razorpay Checkout.
/// Signs with the fake gateway's public secret and goes through the same verification as a real
/// payment, so the storefront's pay flow can be tried end to end without Razorpay keys.
/// </summary>
public sealed record SimulatePaymentCommand(Guid BuyerId, string GatewayOrderId) : ICommand<PaymentResultDto>;

internal sealed class SimulatePaymentCommandHandler(IPaymentGateway gateway, IDispatcher dispatcher)
    : ICommandHandler<SimulatePaymentCommand, PaymentResultDto>
{
    public async Task<Result<PaymentResultDto>> HandleAsync(
        SimulatePaymentCommand command,
        CancellationToken cancellationToken)
    {
        if (gateway is not FakeGateway)
        {
            return Result.Failure<PaymentResultDto>(PaymentErrors.FakeGatewayOnly);
        }

        var paymentId = $"pay_fake_{Guid.NewGuid():N}"[..23];
        var signature = RazorpaySignature.ForPayment(command.GatewayOrderId, paymentId, FakeGateway.KeySecret);

        return await dispatcher.SendAsync(
            new VerifyPaymentCommand(command.BuyerId, command.GatewayOrderId, paymentId, signature),
            cancellationToken);
    }
}
