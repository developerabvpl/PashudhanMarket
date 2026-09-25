using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Application;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Services;

/// <summary>Implements <see cref="IOrderFulfilmentService"/> for Shipping.</summary>
internal sealed class OrderFulfilmentService(
    UPBazaarDbContext dbContext,
    OrderTransaction transaction,
    OrderReader reader,
    IClock clock,
    IOptions<OrdersModuleOptions> options) : IOrderFulfilmentService
{
    public async Task<Result<ShippablePartDto>> GetPartAsync(
        Guid orderId,
        Guid partId,
        CancellationToken cancellationToken)
    {
        var order = await reader.FindReadOnlyAsync(orderId, buyerId: null, cancellationToken);
        var part = order?.Parts.FirstOrDefault(p => p.PublicId == partId);

        if (order is null || part is null)
        {
            return Result.Failure<ShippablePartDto>(order is null ? OrderErrors.NotFound : OrderErrors.PartNotFound);
        }

        var dto = order.ToDto();
        var partDto = dto.Parts.Single(p => p.Id == partId);

        return new ShippablePartDto(
            order.PublicId,
            order.Number,
            order.BuyerId,
            part.PublicId,
            part.SellerId,
            part.Status.ToString(),
            order.PaymentMethod.ToString(),
            order.PaymentMethod == PaymentMethod.CashOnDelivery ? part.AmountDue : 0m,
            part.Subtotal,
            part.DeliveryFee,
            order.Currency,
            order.PlacedAtUtc,
            dto.DeliveryAddress,
            partDto.Lines,
            part.IsBuyerReturn);
    }

    public async Task<Result> AdvancePartAsync(
        Guid orderId,
        Guid partId,
        string status,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<OrderPartStatus>(status, ignoreCase: true, out var target)
            || target is not (OrderPartStatus.Packed or OrderPartStatus.Shipped or OrderPartStatus.Delivered
                or OrderPartStatus.Returning or OrderPartStatus.Returned))
        {
            return Result.Failure(OrderErrors.InvalidTransition);
        }

        var result = await transaction.RunAsync(async ct =>
        {
            var order = await reader.FindAsync(orderId, ct);
            var part = order?.Parts.FirstOrDefault(p => p.PublicId == partId);

            if (order is null || part is null)
            {
                return Result.Failure<bool>(order is null ? OrderErrors.NotFound : OrderErrors.PartNotFound);
            }

            // Already there or past it: a late or repeated courier update, not an error.
            if (part.Status != OrderPartStatus.Cancelled && part.Status >= target)
            {
                return Result.Success(true);
            }

            var moved = target switch
            {
                OrderPartStatus.Returning => order.StartReturn(partId),
                OrderPartStatus.Returned => order.CompleteReturn(partId, clock.UtcNow),
                _ => order.AdvancePart(partId, target, clock.UtcNow, options.Value.ReturnWindow),
            };

            if (moved.IsFailure)
            {
                return Result.Failure<bool>(moved.Error);
            }

            await dbContext.SaveChangesAsync(ct);

            return Result.Success(true);
        }, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }
}
