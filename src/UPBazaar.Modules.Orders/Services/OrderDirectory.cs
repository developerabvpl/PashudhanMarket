using UPBazaar.Modules.Orders.Application;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Services;

/// <summary>Implements <see cref="IOrderDirectory"/>.</summary>
internal sealed class OrderDirectory(OrderReader reader) : IOrderDirectory
{
    public async Task<Result<OrderNoticeDto>> GetNoticeAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await reader.FindReadOnlyAsync(orderId, buyerId: null, cancellationToken);

        if (order is null)
        {
            return Result.Failure<OrderNoticeDto>(OrderErrors.NotFound);
        }

        return new OrderNoticeDto(
            order.PublicId,
            order.Number,
            order.BuyerId,
            order.DeliveryAddress.Mobile,
            order.Total,
            order.Currency,
            order.PaymentMethod.ToString(),
            [.. order.Parts.OrderBy(p => p.Id).Select(p => new OrderNoticePartDto(p.PublicId, p.SellerId, p.Subtotal, p.Lines.Sum(l => l.Quantity)))]);
    }
}
