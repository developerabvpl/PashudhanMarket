using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Settlements.Contracts.Dtos;
using UPBazaar.Modules.Settlements.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Settlements.Application;

/// <summary>
/// Payouts, for finance or one seller. Pending ones oldest first, since each is a seller waiting
/// for money; the rest newest first.
/// </summary>
public sealed record ListPayoutsQuery(int Page, int PageSize, string? Status, Guid? SellerId) : IQuery<PagedList<PayoutSummaryDto>>;

internal sealed class ListPayoutsQueryValidator : AbstractValidator<ListPayoutsQuery>
{
    public ListPayoutsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<PayoutStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Pending or Paid.");
    }
}

internal sealed class ListPayoutsQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListPayoutsQuery, PagedList<PayoutSummaryDto>>
{
    public async Task<Result<PagedList<PayoutSummaryDto>>> HandleAsync(ListPayoutsQuery query, CancellationToken cancellationToken)
    {
        var payouts = dbContext.Set<Payout>().AsNoTracking();

        if (query.SellerId is { } seller)
        {
            payouts = payouts.Where(p => p.SellerId == seller);
        }

        var filtered = Enum.TryParse<PayoutStatus>(query.Status, ignoreCase: true, out var status);

        if (filtered)
        {
            payouts = payouts.Where(p => p.Status == status);
        }

        var total = await payouts.CountAsync(cancellationToken);

        var ordered = filtered && status == PayoutStatus.Pending
            ? payouts.OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            : payouts.OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id);

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new PayoutSummaryDto(
                p.PublicId,
                p.SellerId,
                p.ShopName,
                p.GrossAmount,
                p.NetAmount,
                p.EarningCount,
                p.Currency,
                p.Status.ToString(),
                p.CreatedAtUtc,
                p.PaidAtUtc,
                p.Utr))
            .ToListAsync(cancellationToken);

        return new PagedList<PayoutSummaryDto>(items, query.Page, query.PageSize, total);
    }
}

/// <summary>One payout with its earnings.</summary>
/// <param name="PayoutId">The payout.</param>
/// <param name="SellerId">Set when a seller asks: it must be theirs, and they see their account masked.</param>
public sealed record GetPayoutQuery(Guid PayoutId, Guid? SellerId) : IQuery<PayoutDto>;

internal sealed class GetPayoutQueryHandler(UPBazaarDbContext dbContext) : IQueryHandler<GetPayoutQuery, PayoutDto>
{
    public async Task<Result<PayoutDto>> HandleAsync(GetPayoutQuery query, CancellationToken cancellationToken)
    {
        var payout = await dbContext.Set<Payout>()
            .AsNoTracking()
            .Include(p => p.Earnings)
            .FirstOrDefaultAsync(p => p.PublicId == query.PayoutId, cancellationToken);

        if (payout is null || (query.SellerId is { } seller && payout.SellerId != seller))
        {
            return Result.Failure<PayoutDto>(SettlementErrors.PayoutNotFound);
        }

        return payout.ToDto(maskAccount: query.SellerId is not null);
    }
}

/// <summary>Finance record a payout as transferred, with the bank's transaction reference.</summary>
public sealed record MarkPayoutPaidCommand(Guid PayoutId, string Utr) : ICommand<PayoutDto>;

internal sealed class MarkPayoutPaidCommandValidator : AbstractValidator<MarkPayoutPaidCommand>
{
    public MarkPayoutPaidCommandValidator()
    {
        RuleFor(x => x.PayoutId).NotEmpty();

        // NEFT UTRs run to 16 characters, IMPS references to 12 digits; letters and digits only.
        RuleFor(x => x.Utr).NotEmpty().MaximumLength(32)
            .Matches("^[A-Za-z0-9]+$")
            .WithMessage("Enter the transaction reference as the bank shows it: letters and digits only.");
    }
}

internal sealed class MarkPayoutPaidCommandHandler(UPBazaarDbContext dbContext, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<MarkPayoutPaidCommand, PayoutDto>
{
    public async Task<Result<PayoutDto>> HandleAsync(MarkPayoutPaidCommand command, CancellationToken cancellationToken)
    {
        var payout = await dbContext.Set<Payout>()
            .Include(p => p.Earnings)
            .FirstOrDefaultAsync(p => p.PublicId == command.PayoutId, cancellationToken);

        if (payout is null)
        {
            return Result.Failure<PayoutDto>(SettlementErrors.PayoutNotFound);
        }

        var paid = payout.MarkPaid(command.Utr, currentUser.UserId, clock.UtcNow);

        if (paid.IsFailure)
        {
            return Result.Failure<PayoutDto>(paid.Error);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<PayoutDto>(SettlementErrors.ConcurrentChange);
        }

        return payout.ToDto(maskAccount: false);
    }
}

/// <summary>Earnings, newest delivery first, for finance or one seller.</summary>
public sealed record ListEarningsQuery(int Page, int PageSize, string? Status, Guid? SellerId) : IQuery<PagedList<EarningDto>>;

internal sealed class ListEarningsQueryValidator : AbstractValidator<ListEarningsQuery>
{
    public ListEarningsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<EarningStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Accruing, OnHold, Cancelled or Settled.");
    }
}

internal sealed class ListEarningsQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListEarningsQuery, PagedList<EarningDto>>
{
    public async Task<Result<PagedList<EarningDto>>> HandleAsync(ListEarningsQuery query, CancellationToken cancellationToken)
    {
        var earnings =
            from earning in dbContext.Set<Earning>().AsNoTracking()
            join p in dbContext.Set<Payout>() on earning.PayoutId equals p.Id into payouts
            from payout in payouts.DefaultIfEmpty()
            select new { earning, payout };

        if (query.SellerId is { } seller)
        {
            earnings = earnings.Where(x => x.earning.SellerId == seller);
        }

        if (Enum.TryParse<EarningStatus>(query.Status, ignoreCase: true, out var status))
        {
            earnings = earnings.Where(x => x.earning.Status == status);
        }

        var total = await earnings.CountAsync(cancellationToken);

        var items = await earnings
            .OrderByDescending(x => x.earning.DeliveredAtUtc)
            .ThenByDescending(x => x.earning.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new EarningDto(
                x.earning.PublicId,
                x.earning.SellerId,
                x.earning.OrderId,
                x.earning.OrderNumber,
                x.earning.OrderPartId,
                x.earning.GrossAmount,
                x.earning.CommissionPercent,
                x.earning.CommissionAmount,
                x.earning.TcsAmount,
                x.earning.TdsAmount,
                x.earning.NetAmount,
                x.earning.Currency,
                x.earning.Status.ToString(),
                x.earning.DeliveredAtUtc,
                x.earning.PayableFromUtc,
                x.payout != null ? (Guid?)x.payout.PublicId : null))
            .ToListAsync(cancellationToken);

        return new PagedList<EarningDto>(items, query.Page, query.PageSize, total);
    }
}

/// <summary>What a seller has coming, by where it stands.</summary>
public sealed record GetSellerBalanceQuery(Guid SellerId) : IQuery<SellerBalanceDto>;

internal sealed class GetSellerBalanceQueryHandler(UPBazaarDbContext dbContext, IClock clock)
    : IQueryHandler<GetSellerBalanceQuery, SellerBalanceDto>
{
    public async Task<Result<SellerBalanceDto>> HandleAsync(GetSellerBalanceQuery query, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var earnings = dbContext.Set<Earning>().AsNoTracking().Where(e => e.SellerId == query.SellerId);
        var payouts = dbContext.Set<Payout>().AsNoTracking().Where(p => p.SellerId == query.SellerId);

        return new SellerBalanceDto(
            await earnings.Where(e => e.Status == EarningStatus.Accruing && e.PayableFromUtc > now).SumAsync(e => e.NetAmount, cancellationToken),
            await earnings.Where(e => e.Status == EarningStatus.OnHold).SumAsync(e => e.NetAmount, cancellationToken),
            await earnings.Where(e => e.Status == EarningStatus.Accruing && e.PayableFromUtc <= now).SumAsync(e => e.NetAmount, cancellationToken),
            await payouts.Where(p => p.Status == PayoutStatus.Pending).SumAsync(p => p.NetAmount, cancellationToken),
            await payouts.Where(p => p.Status == PayoutStatus.Paid).SumAsync(p => p.NetAmount, cancellationToken),
            "INR");
    }
}
