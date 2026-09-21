using UPBazaar.Modules.Inventory.Contracts;
using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Application;

/// <summary>
/// Gives back the stock of whatever an order no longer needs.
///
/// Which Inventory call that is depends on how far the order got. Before payment the stock is
/// only held, so the hold is released. After confirmation it was committed, so it has to be put
/// back on hand, part by part. Always called inside an <see cref="OrderTransaction"/>, after the
/// order itself has been changed.
/// </summary>
internal sealed class OrderStock(IInventoryService inventory)
{
    public async Task<Result> GiveBackAsync(
        Order order,
        bool wasAwaitingPayment,
        IReadOnlyCollection<OrderPart> cancelledParts,
        CancellationToken cancellationToken)
    {
        if (wasAwaitingPayment)
        {
            return await inventory.ReleaseAsync(order.ReservationId, cancellationToken);
        }

        var lines = cancelledParts
            .SelectMany(p => p.Lines)
            .GroupBy(l => l.ProductId)
            .Select(g => new ReservationLineDto(g.Key, g.Sum(l => l.Quantity)))
            .ToList();

        return lines.Count == 0
            ? Result.Success()
            : await inventory.ReturnAsync(order.Number, lines, cancellationToken);
    }
}
