using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Ordering.Contracts.Dtos;
using UPBazaar.Modules.Ordering.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Ordering.Application.Orders;

public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

internal sealed class GetOrderQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async Task<Result<OrderDto>> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken)
    {
        var order = await dbContext.Set<Order>()
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.PublicId == query.OrderId, cancellationToken);

        return order is null ? Result.Failure<OrderDto>(OrderErrors.NotFound) : order.ToDto();
    }
}
