using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.ExternalServices.Payments;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Infrastructure.Persistence.Idempotency;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;
using System.Text.Json;

namespace UPBazaar.Modules.Payments.Application.Payments;

public sealed record RefundPaymentCommand(
    Guid PaymentId,
    decimal Amount,
    string Reason,
    string IdempotencyKey) : ICommand<RefundDto>;

internal sealed class RefundPaymentCommandValidator : AbstractValidator<RefundPaymentCommand>
{
    public RefundPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(256);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
    }
}

/// <summary>
/// Refunds are the one operation where a duplicate costs real money, so the idempotency key
/// is reserved before the gateway is ever called.
/// </summary>
internal sealed class RefundPaymentCommandHandler(
    UPBazaarDbContext dbContext,
    IPaymentGateway gateway,
    IIdempotencyService idempotency,
    IClock clock) : ICommandHandler<RefundPaymentCommand, RefundDto>
{
    private const string Endpoint = "payments.refund";

    public async Task<Result<RefundDto>> HandleAsync(
        RefundPaymentCommand command,
        CancellationToken cancellationToken)
    {
        var requestHash = IdempotencyService.Hash(
            JsonSerializer.Serialize(command with { IdempotencyKey = string.Empty }));

        var reservation = await idempotency.TryBeginAsync(
            command.IdempotencyKey,
            Endpoint,
            requestHash,
            cancellationToken);

        if (reservation.IsConflict)
        {
            return Result.Failure<RefundDto>(PaymentErrors.IdempotencyConflict);
        }

        if (reservation.IsReplay)
        {
            return reservation.ReplayPayload is null
                ? Result.Failure<RefundDto>(Error.Conflict(
                    "payments.refund.in_flight",
                    "The original refund with this idempotency key is still being processed."))
                : Result.Success(JsonSerializer.Deserialize<RefundDto>(reservation.ReplayPayload)!);
        }

        var payment = await dbContext.Set<Payment>()
            .FirstOrDefaultAsync(p => p.PublicId == command.PaymentId, cancellationToken);

        if (payment is null)
        {
            return Result.Failure<RefundDto>(PaymentErrors.NotFound);
        }

        if (payment.GatewayPaymentId is null)
        {
            return Result.Failure<RefundDto>(PaymentErrors.NotRefundable);
        }

        if (command.Amount > payment.RefundableAmount)
        {
            return Result.Failure<RefundDto>(PaymentErrors.RefundExceedsCaptured);
        }

        var gatewayRefund = await gateway.RefundAsync(
            new GatewayRefundRequest(
                command.IdempotencyKey,
                payment.GatewayPaymentId,
                command.Amount,
                command.Reason),
            cancellationToken);

        var issued = payment.IssueRefund(gatewayRefund.GatewayRefundId, command.Amount, command.Reason);

        if (issued.IsFailure)
        {
            return Result.Failure<RefundDto>(issued.Error);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<RefundDto>(PaymentErrors.ConcurrencyConflict);
        }

        var dto = issued.Value.ToDto(payment.PublicId) with { CreatedAtUtc = clock.UtcNow };

        await idempotency.CompleteAsync(
            command.IdempotencyKey,
            JsonSerializer.Serialize(dto),
            cancellationToken);

        return dto;
    }
}
