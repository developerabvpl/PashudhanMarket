using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Application;

/// <summary>
/// A seller's parts of orders, oldest first by default, since the queue a seller works through is
/// what has waited longest. Awaiting-payment parts are left out: the seller must not pack those.
/// </summary>
public sealed record ListSellerOrdersQuery(Guid SellerId, int Page, int PageSize, string? Status)
    : IQuery<PagedList<SellerOrderSummaryDto>>;

internal sealed class ListSellerOrdersQueryValidator : AbstractValidator<ListSellerOrdersQuery>
{
    public ListSellerOrdersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(s => s is null || (Enum.TryParse<OrderPartStatus>(s, ignoreCase: true, out var status) && status != OrderPartStatus.AwaitingPayment))
            .WithMessage("Status must be Confirmed, Packed, Shipped, Delivered or Cancelled.");
    }
}

internal sealed class ListSellerOrdersQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListSellerOrdersQuery, PagedList<SellerOrderSummaryDto>>
{
    public async Task<Result<PagedList<SellerOrderSummaryDto>>> HandleAsync(
        ListSellerOrdersQuery query,
        CancellationToken cancellationToken)
    {
        var parts =
            from order in dbContext.Set<Order>().AsNoTracking()
            from part in order.Parts
            where part.SellerId == query.SellerId && part.Status != OrderPartStatus.AwaitingPayment
            select new { order, part };

        var filtered = Enum.TryParse<OrderPartStatus>(query.Status, ignoreCase: true, out var status);

        if (filtered)
        {
            parts = parts.Where(x => x.part.Status == status);
        }

        var total = await parts.CountAsync(cancellationToken);

        // Work still to do is oldest first; finished work newest first, for looking something up.
        var ordered = !filtered || status is OrderPartStatus.Confirmed or OrderPartStatus.Packed
            ? parts.OrderBy(x => x.order.PlacedAtUtc)
            : parts.OrderByDescending(x => x.order.PlacedAtUtc);

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new SellerOrderSummaryDto(
                x.order.PublicId,
                x.order.Number,
                x.part.PublicId,
                x.part.Status.ToString(),
                x.order.PaymentMethod.ToString(),
                x.order.PaymentMethod == PaymentMethod.CashOnDelivery ? x.part.Lines.Sum(l => l.UnitPrice * l.Quantity) : 0m,
                x.part.Lines.Sum(l => l.UnitPrice * l.Quantity),
                x.order.Currency,
                x.part.Lines.Sum(l => l.Quantity),
                x.order.DeliveryAddress.City,
                x.order.PlacedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedList<SellerOrderSummaryDto>(items, query.Page, query.PageSize, total);
    }
}

/// <summary>A seller's part of one order, with what to pack and where it goes.</summary>
public sealed record GetSellerOrderQuery(Guid SellerId, Guid OrderId) : IQuery<SellerOrderDto>;

internal sealed class GetSellerOrderQueryHandler(OrderReader reader) : IQueryHandler<GetSellerOrderQuery, SellerOrderDto>
{
    public async Task<Result<SellerOrderDto>> HandleAsync(GetSellerOrderQuery query, CancellationToken cancellationToken)
    {
        var order = await reader.FindReadOnlyAsync(query.OrderId, buyerId: null, cancellationToken);
        var part = order?.Parts.FirstOrDefault(p => p.SellerId == query.SellerId && p.Status != OrderPartStatus.AwaitingPayment);

        if (order is null || part is null)
        {
            return Result.Failure<SellerOrderDto>(OrderErrors.NotFound);
        }

        var dto = order.ToDto();
        var partDto = dto.Parts.Single(p => p.Id == part.PublicId);

        return new SellerOrderDto(
            order.PublicId,
            order.Number,
            part.PublicId,
            part.Status.ToString(),
            order.PaymentMethod.ToString(),
            order.PaymentMethod == PaymentMethod.CashOnDelivery ? part.Subtotal : 0m,
            part.Subtotal,
            order.Currency,
            order.PlacedAtUtc,
            dto.DeliveryAddress,
            partDto.Lines,
            part.CancellationReason);
    }
}
