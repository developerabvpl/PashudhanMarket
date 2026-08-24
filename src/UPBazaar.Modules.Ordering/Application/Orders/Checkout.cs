using System.Text.Json;
using FluentValidation;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Infrastructure.Persistence.Idempotency;
using UPBazaar.Modules.Catalog.Contracts;
using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Ordering.Contracts.Dtos;
using UPBazaar.Modules.Ordering.Domain;
using UPBazaar.Modules.Payments.Contracts;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Ordering.Application.Orders;

public sealed record CheckoutLine(Guid ProductId, int Quantity);

/// <param name="IdempotencyKey">
/// Taken from the Idempotency-Key header. Retrying with the same key replays the original
/// order instead of placing a second one.
/// </param>
public sealed record CheckoutCommand(
    Guid CustomerId,
    IReadOnlyList<CheckoutLine> Lines,
    string DeliveryPostcode,
    Guid SellerId,
    string IdempotencyKey) : ICommand<OrderDto>;

internal sealed class CheckoutCommandValidator : AbstractValidator<CheckoutCommand>
{
    public CheckoutCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.SellerId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(x => x.DeliveryPostcode).NotEmpty().Matches("^[0-9]{6}$")
            .WithMessage("Delivery postcode must be a 6-digit Indian PIN code.");
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0).LessThanOrEqualTo(100);
        });
    }
}

/// <summary>
/// Reserves stock, creates the order, then opens a payment. All three steps hang off one
/// idempotency key, so a retried checkout neither double-reserves nor double-charges.
/// </summary>
internal sealed class CheckoutCommandHandler(
    UPBazaarDbContext dbContext,
    IStockReservations stockReservations,
    IPaymentInitiation paymentInitiation,
    IIdempotencyService idempotency,
    IClock clock) : ICommandHandler<CheckoutCommand, OrderDto>
{
    private const string Endpoint = "ordering.checkout";

    /// <summary>Flat national shipping fee until seller rate cards land.</summary>
    private const decimal ShippingFee = 49.00m;

    public async Task<Result<OrderDto>> HandleAsync(
        CheckoutCommand command,
        CancellationToken cancellationToken)
    {
        // The key itself is excluded so that the same cart under a new key still hashes alike.
        var requestHash = IdempotencyService.Hash(
            JsonSerializer.Serialize(command with { IdempotencyKey = string.Empty }));

        var reservation = await idempotency.TryBeginAsync(
            command.IdempotencyKey,
            Endpoint,
            requestHash,
            cancellationToken);

        if (reservation.IsConflict)
        {
            return Result.Failure<OrderDto>(OrderErrors.IdempotencyConflict);
        }

        if (reservation.IsReplay)
        {
            return reservation.ReplayPayload is null
                ? Result.Failure<OrderDto>(OrderErrors.CheckoutInFlight)
                : Result.Success(JsonSerializer.Deserialize<OrderDto>(reservation.ReplayPayload)!);
        }

        var stock = await stockReservations.ReserveAsync(
            command.Lines.Select(l => new StockReservationLine(l.ProductId, l.Quantity)).ToList(),
            cancellationToken);

        if (stock.IsFailure)
        {
            return Result.Failure<OrderDto>(stock.Error);
        }

        var reserved = stock.Value;
        var currencies = reserved.Select(r => r.Currency).Distinct().ToList();

        if (currencies.Count > 1)
        {
            return Result.Failure<OrderDto>(OrderErrors.MixedCurrency);
        }

        var placed = Order.Place(
            command.CustomerId,
            Order.NextOrderNumber(clock.UtcNow),
            command.DeliveryPostcode,
            currencies[0],
            ShippingFee,
            reserved.Select(r => (r.ProductId, r.Sku, r.Name, r.UnitPrice, r.Quantity)).ToList(),
            clock.UtcNow);

        if (placed.IsFailure)
        {
            return Result.Failure<OrderDto>(placed.Error);
        }

        var order = placed.Value;

        var payment = await paymentInitiation.CreateForOrderAsync(
            new CreatePaymentForOrderRequest(
                order.PublicId,
                order.OrderNumber,
                command.SellerId,
                order.Total,
                order.Currency,
                command.IdempotencyKey),
            cancellationToken);

        if (payment.IsFailure)
        {
            return Result.Failure<OrderDto>(payment.Error);
        }

        order.AttachPayment(payment.Value.PaymentId, payment.Value.GatewayOrderId);

        dbContext.Set<Order>().Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);

        var dto = order.ToDto();

        await idempotency.CompleteAsync(
            command.IdempotencyKey,
            JsonSerializer.Serialize(dto),
            cancellationToken);

        return dto;
    }
}
