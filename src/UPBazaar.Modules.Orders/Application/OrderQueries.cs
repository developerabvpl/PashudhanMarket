using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Application;

/// <summary>
/// One order. With a buyer id it is that buyer's own or not found; without one (staff) any order.
/// </summary>
public sealed record GetOrderQuery(Guid OrderId, Guid? BuyerId) : IQuery<OrderDto>;

internal sealed class GetOrderQueryHandler(OrderReader reader) : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async Task<Result<OrderDto>> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken)
    {
        var order = await reader.FindReadOnlyAsync(query.OrderId, query.BuyerId, cancellationToken);

        return order is null ? Result.Failure<OrderDto>(OrderErrors.NotFound) : order.ToDto();
    }
}

/// <summary>
/// A page of orders, newest first. With a buyer id, only theirs. Staff may also filter by status
/// and look an order up by (part of) its number, which is what a buyer reads out on the phone.
/// </summary>
public sealed record ListOrdersQuery(
    int Page,
    int PageSize,
    Guid? BuyerId,
    string? Status,
    string? Number) : IQuery<PagedList<OrderSummaryDto>>;

internal sealed class ListOrdersQueryValidator : AbstractValidator<ListOrdersQuery>
{
    public ListOrdersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Number).MaximumLength(OrderNumber.MaxLength);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<OrderStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be PendingPayment, Confirmed, Completed or Cancelled.");
    }
}

internal sealed class ListOrdersQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListOrdersQuery, PagedList<OrderSummaryDto>>
{
    public async Task<Result<PagedList<OrderSummaryDto>>> HandleAsync(
        ListOrdersQuery query,
        CancellationToken cancellationToken)
    {
        var orders = dbContext.Set<Order>().AsNoTracking();

        if (query.BuyerId is { } buyerId)
        {
            orders = orders.Where(o => o.BuyerId == buyerId);
        }

        if (Enum.TryParse<OrderStatus>(query.Status, ignoreCase: true, out var status))
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Number))
        {
            var number = query.Number.Trim().ToUpperInvariant();
            orders = orders.Where(o => o.Number.Contains(number));
        }

        var total = await orders.CountAsync(cancellationToken);

        var items = await OrderReader.Summaries(orders)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<OrderSummaryDto>(items, query.Page, query.PageSize, total);
    }
}
