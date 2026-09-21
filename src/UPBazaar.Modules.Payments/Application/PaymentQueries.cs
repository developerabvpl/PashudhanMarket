using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Payments.Contracts.Dtos;
using UPBazaar.Modules.Payments.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Payments.Application;

/// <summary>
/// Payments for staff, newest first. The search matches an order number or either gateway id,
/// which covers the three things a support call or a Razorpay dashboard row gives you.
/// </summary>
public sealed record ListPaymentsQuery(int Page, int PageSize, string? Status, string? Search)
    : IQuery<PagedList<PaymentDto>>;

internal sealed class ListPaymentsQueryValidator : AbstractValidator<ListPaymentsQuery>
{
    public ListPaymentsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(64);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<PaymentStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Created or Paid.");
    }
}

internal sealed class ListPaymentsQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListPaymentsQuery, PagedList<PaymentDto>>
{
    public async Task<Result<PagedList<PaymentDto>>> HandleAsync(
        ListPaymentsQuery query,
        CancellationToken cancellationToken)
    {
        var payments = dbContext.Set<Payment>().AsNoTracking();

        if (Enum.TryParse<PaymentStatus>(query.Status, ignoreCase: true, out var status))
        {
            payments = payments.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            payments = payments.Where(p =>
                p.OrderNumber.Contains(search)
                || p.GatewayOrderId == search
                || p.GatewayPaymentId == search);
        }

        var total = await payments.CountAsync(cancellationToken);

        var items = await payments
            .OrderByDescending(p => p.CreatedAtUtc)
            .ThenByDescending(p => p.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new PaymentDto(
                p.PublicId,
                p.OrderId,
                p.OrderNumber,
                p.BuyerId,
                p.Amount,
                p.Currency,
                p.Status.ToString(),
                p.OrderOutcome.ToString(),
                p.Gateway,
                p.GatewayOrderId,
                p.GatewayPaymentId,
                p.LastFailure,
                p.CreatedAtUtc,
                p.PaidAtUtc,
                p.Refunds.Where(r => r.Status == RefundStatus.Due).Sum(r => r.Amount)))
            .ToListAsync(cancellationToken);

        return new PagedList<PaymentDto>(items, query.Page, query.PageSize, total);
    }
}

/// <summary>Refunds, oldest first when filtering to Due: the longest-waiting buyer comes first.</summary>
public sealed record ListRefundsQuery(int Page, int PageSize, string? Status) : IQuery<PagedList<RefundDto>>;

internal sealed class ListRefundsQueryValidator : AbstractValidator<ListRefundsQuery>
{
    public ListRefundsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<RefundStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Due or Refunded.");
    }
}

internal sealed class ListRefundsQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListRefundsQuery, PagedList<RefundDto>>
{
    public async Task<Result<PagedList<RefundDto>>> HandleAsync(
        ListRefundsQuery query,
        CancellationToken cancellationToken)
    {
        var refunds =
            from payment in dbContext.Set<Payment>().AsNoTracking()
            from refund in payment.Refunds
            select new { payment, refund };

        var filtered = Enum.TryParse<RefundStatus>(query.Status, ignoreCase: true, out var status);

        if (filtered)
        {
            refunds = refunds.Where(x => x.refund.Status == status);
        }

        var ordered = filtered && status == RefundStatus.Due
            ? refunds.OrderBy(x => x.refund.CreatedAtUtc)
            : refunds.OrderByDescending(x => x.refund.CreatedAtUtc);

        var total = await refunds.CountAsync(cancellationToken);

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new RefundDto(
                x.refund.PublicId,
                x.payment.PublicId,
                x.payment.OrderId,
                x.payment.OrderNumber,
                x.refund.OrderPartId,
                x.refund.Amount,
                x.payment.Currency,
                x.refund.Reason,
                x.refund.Status.ToString(),
                x.payment.GatewayPaymentId,
                x.refund.GatewayRefundId,
                x.refund.CreatedAtUtc,
                x.refund.RefundedAtUtc,
                x.refund.RefundedBy))
            .ToListAsync(cancellationToken);

        return new PagedList<RefundDto>(items, query.Page, query.PageSize, total);
    }
}

/// <summary>Maps refunds to the DTO staff see.</summary>
internal static class PaymentMappings
{
    public static RefundDto ToDto(this Refund refund, Payment payment) => new(
        refund.PublicId,
        payment.PublicId,
        payment.OrderId,
        payment.OrderNumber,
        refund.OrderPartId,
        refund.Amount,
        payment.Currency,
        refund.Reason,
        refund.Status.ToString(),
        payment.GatewayPaymentId,
        refund.GatewayRefundId,
        refund.CreatedAtUtc,
        refund.RefundedAtUtc,
        refund.RefundedBy);
}
