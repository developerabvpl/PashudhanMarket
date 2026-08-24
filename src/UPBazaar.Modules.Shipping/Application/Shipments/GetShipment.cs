using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Application.Shipments;

public sealed record GetShipmentQuery(Guid ShipmentId) : IQuery<ShipmentDto>;

internal sealed class GetShipmentQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<GetShipmentQuery, ShipmentDto>
{
    public async Task<Result<ShipmentDto>> HandleAsync(
        GetShipmentQuery query,
        CancellationToken cancellationToken)
    {
        var shipment = await dbContext.Set<Shipment>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.PublicId == query.ShipmentId, cancellationToken);

        return shipment is null
            ? Result.Failure<ShipmentDto>(ShippingErrors.NotFound)
            : shipment.ToDto();
    }
}

public sealed record GetShipmentByOrderQuery(Guid OrderId) : IQuery<ShipmentDto>;

internal sealed class GetShipmentByOrderQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<GetShipmentByOrderQuery, ShipmentDto>
{
    public async Task<Result<ShipmentDto>> HandleAsync(
        GetShipmentByOrderQuery query,
        CancellationToken cancellationToken)
    {
        var shipment = await dbContext.Set<Shipment>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrderId == query.OrderId, cancellationToken);

        return shipment is null
            ? Result.Failure<ShipmentDto>(ShippingErrors.NotFound)
            : shipment.ToDto();
    }
}
