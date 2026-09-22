using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Application;

/// <summary>
/// The shipments for one order. With a buyer id, only if the order is theirs - an order they do
/// not own answers with nothing, exactly like an order not yet shipped.
/// </summary>
public sealed record GetOrderShipmentsQuery(Guid OrderId, Guid? BuyerId) : IQuery<IReadOnlyList<ShipmentDto>>;

internal sealed class GetOrderShipmentsQueryHandler(UPBazaarDbContext dbContext, ICourierGateway courier)
    : IQueryHandler<GetOrderShipmentsQuery, IReadOnlyList<ShipmentDto>>
{
    public async Task<Result<IReadOnlyList<ShipmentDto>>> HandleAsync(
        GetOrderShipmentsQuery query,
        CancellationToken cancellationToken)
    {
        var shipments = await dbContext.Set<Shipment>()
            .AsNoTracking()
            .Include(s => s.Events)
            .Where(s => s.OrderId == query.OrderId && (query.BuyerId == null || s.BuyerId == query.BuyerId))
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ShipmentDto>>([.. shipments.Select(s => s.ToDto(courier))]);
    }
}

/// <summary>Shipments for staff, newest first, searchable by order number or AWB.</summary>
public sealed record ListShipmentsQuery(int Page, int PageSize, string? Status, string? Search)
    : IQuery<PagedList<ShipmentDto>>;

internal sealed class ListShipmentsQueryValidator : AbstractValidator<ListShipmentsQuery>
{
    public ListShipmentsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(64);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<ShipmentStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Booking, PickupRequested, InTransit, Delivered, Returned or Cancelled.");
    }
}

internal sealed class ListShipmentsQueryHandler(UPBazaarDbContext dbContext, ICourierGateway courier)
    : IQueryHandler<ListShipmentsQuery, PagedList<ShipmentDto>>
{
    public async Task<Result<PagedList<ShipmentDto>>> HandleAsync(
        ListShipmentsQuery query,
        CancellationToken cancellationToken)
    {
        var shipments = dbContext.Set<Shipment>().AsNoTracking();

        if (Enum.TryParse<ShipmentStatus>(query.Status, ignoreCase: true, out var status))
        {
            shipments = shipments.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToUpperInvariant();
            shipments = shipments.Where(s => s.OrderNumber.Contains(search) || s.Awb == search);
        }

        var total = await shipments.CountAsync(cancellationToken);

        var page = await shipments
            .Include(s => s.Events)
            .OrderByDescending(s => s.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<ShipmentDto>([.. page.Select(s => s.ToDto(courier))], query.Page, query.PageSize, total);
    }
}

/// <summary>Every pickup location: the platform warehouse first, then sellers'.</summary>
public sealed record ListPickupLocationsQuery : IQuery<IReadOnlyList<PickupLocationDto>>;

internal sealed class ListPickupLocationsQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListPickupLocationsQuery, IReadOnlyList<PickupLocationDto>>
{
    public async Task<Result<IReadOnlyList<PickupLocationDto>>> HandleAsync(
        ListPickupLocationsQuery query,
        CancellationToken cancellationToken)
    {
        var locations = await dbContext.Set<PickupLocation>()
            .AsNoTracking()
            .OrderBy(l => l.SellerId != null)
            .ThenBy(l => l.Name)
            .Select(l => new PickupLocationDto(l.SellerId, l.Name, l.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return locations;
    }
}

/// <summary>Sets a seller's pickup location, or the platform warehouse's when the seller is null.</summary>
public sealed record SetPickupLocationCommand(Guid? SellerId, string Name) : ICommand<PickupLocationDto>;

internal sealed class SetPickupLocationCommandValidator : AbstractValidator<SetPickupLocationCommand>
{
    public SetPickupLocationCommandValidator()
    {
        RuleFor(x => x.SellerId).NotEqual(Guid.Empty);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(PickupLocation.NameMaxLength);
    }
}

internal sealed class SetPickupLocationCommandHandler(UPBazaarDbContext dbContext, IClock clock)
    : ICommandHandler<SetPickupLocationCommand, PickupLocationDto>
{
    public async Task<Result<PickupLocationDto>> HandleAsync(
        SetPickupLocationCommand command,
        CancellationToken cancellationToken)
    {
        var location = await dbContext.Set<PickupLocation>()
            .FirstOrDefaultAsync(l => l.SellerId == command.SellerId, cancellationToken);

        if (location is null)
        {
            location = PickupLocation.Create(command.SellerId, command.Name, clock.UtcNow);
            dbContext.Set<PickupLocation>().Add(location);
        }
        else
        {
            location.Rename(command.Name, clock.UtcNow);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new PickupLocationDto(location.SellerId, location.Name, location.UpdatedAtUtc);
    }
}

/// <summary>Removes a seller's own pickup location; they then ship from the platform warehouse.</summary>
public sealed record RemovePickupLocationCommand(Guid SellerId) : ICommand;

internal sealed class RemovePickupLocationCommandHandler(UPBazaarDbContext dbContext)
    : ICommandHandler<RemovePickupLocationCommand>
{
    public async Task<Result> HandleAsync(RemovePickupLocationCommand command, CancellationToken cancellationToken)
    {
        var location = await dbContext.Set<PickupLocation>()
            .FirstOrDefaultAsync(l => l.SellerId == command.SellerId, cancellationToken);

        if (location is null)
        {
            return Result.Failure(ShippingErrors.PickupLocationNotFound);
        }

        dbContext.Set<PickupLocation>().Remove(location);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
