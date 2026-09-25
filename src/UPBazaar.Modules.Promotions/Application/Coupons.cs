using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Promotions.Contracts.Dtos;
using UPBazaar.Modules.Promotions.Domain;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Promotions.Application;

/// <summary>Creates a coupon: the platform's when <paramref name="SellerId"/> is null, otherwise that seller's own.</summary>
/// <param name="SellerId">The seller running it, or null for the platform.</param>
/// <param name="Code">What buyers type: 4 to 20 letters, digits or hyphens.</param>
/// <param name="Description">What it is for.</param>
/// <param name="FundedBy">Platform or Seller. A seller's own coupon is always Seller.</param>
/// <param name="DiscountType">Percent or Flat.</param>
/// <param name="Value">The percentage (1 to 90), or the rupees off.</param>
/// <param name="MaxDiscount">For a percentage, the most it takes off.</param>
/// <param name="MinOrderValue">The least the goods it covers must come to.</param>
/// <param name="StartsAtUtc">When it can first be used; now when null.</param>
/// <param name="EndsAtUtc">When it stops working; never when null.</param>
/// <param name="TotalLimit">How many orders may use it in all.</param>
/// <param name="PerBuyerLimit">How many orders each buyer may use it on; 1 when null.</param>
public sealed record CreateCouponCommand(
    Guid? SellerId,
    string Code,
    string Description,
    string FundedBy,
    string DiscountType,
    decimal Value,
    decimal? MaxDiscount,
    decimal? MinOrderValue,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    int? TotalLimit,
    int? PerBuyerLimit) : ICommand<CouponDto>;

internal sealed class CreateCouponCommandValidator : AbstractValidator<CreateCouponCommand>
{
    public CreateCouponCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(4, Coupon.CodeMaxLength).Matches("^[A-Za-z0-9-]+$")
            .WithMessage("Use 4 to 20 letters, digits or hyphens.");
        RuleFor(x => x.Description).NotEmpty().MaximumLength(Coupon.DescriptionMaxLength);
        RuleFor(x => x.FundedBy).Must(f => Enum.TryParse<CouponFunding>(f, ignoreCase: true, out _))
            .WithMessage("FundedBy must be Platform or Seller.");
        RuleFor(x => x.DiscountType).Must(t => Enum.TryParse<DiscountType>(t, ignoreCase: true, out _))
            .WithMessage("DiscountType must be Percent or Flat.");
        RuleFor(x => x.Value).GreaterThan(0).PrecisionScale(10, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.Value).LessThanOrEqualTo(90)
            .When(x => string.Equals(x.DiscountType, nameof(Domain.DiscountType.Percent), StringComparison.OrdinalIgnoreCase))
            .WithMessage("A percentage coupon can take at most 90% off.");
        RuleFor(x => x.MaxDiscount).GreaterThan(0).When(x => x.MaxDiscount is not null);
        RuleFor(x => x.MinOrderValue).GreaterThan(0).When(x => x.MinOrderValue is not null);
        RuleFor(x => x.EndsAtUtc).GreaterThan(x => x.StartsAtUtc ?? DateTime.MinValue).When(x => x.EndsAtUtc is not null)
            .WithMessage("It must end after it starts.");
        RuleFor(x => x.TotalLimit).GreaterThan(0).When(x => x.TotalLimit is not null);
        RuleFor(x => x.PerBuyerLimit).InclusiveBetween(1, 100).When(x => x.PerBuyerLimit is not null);
    }
}

internal sealed class CreateCouponCommandHandler(UPBazaarDbContext dbContext, IClock clock)
    : ICommandHandler<CreateCouponCommand, CouponDto>
{
    public async Task<Result<CouponDto>> HandleAsync(CreateCouponCommand command, CancellationToken cancellationToken)
    {
        var code = Coupon.Normalize(command.Code);

        if (await dbContext.Set<Coupon>().AnyAsync(c => c.Code == code, cancellationToken))
        {
            return Result.Failure<CouponDto>(PromotionErrors.CodeTaken);
        }

        var coupon = Coupon.Create(
            code,
            command.Description,
            command.SellerId,
            Enum.Parse<CouponFunding>(command.FundedBy, ignoreCase: true),
            Enum.Parse<DiscountType>(command.DiscountType, ignoreCase: true),
            command.Value,
            command.MaxDiscount,
            command.MinOrderValue,
            command.StartsAtUtc ?? clock.UtcNow,
            command.EndsAtUtc,
            command.TotalLimit,
            command.PerBuyerLimit ?? 1);

        dbContext.Set<Coupon>().Add(coupon);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same code made twice at once; the unique index let one through.
            return Result.Failure<CouponDto>(PromotionErrors.CodeTaken);
        }

        return coupon.ToDto(viewer: command.SellerId);
    }
}

/// <summary>Ends a coupon: it stops working at once. A seller may end only their own.</summary>
public sealed record EndCouponCommand(Guid CouponId, Guid? SellerId) : ICommand<CouponDto>;

internal sealed class EndCouponCommandHandler(UPBazaarDbContext dbContext) : ICommandHandler<EndCouponCommand, CouponDto>
{
    public async Task<Result<CouponDto>> HandleAsync(EndCouponCommand command, CancellationToken cancellationToken)
    {
        var coupon = await dbContext.Set<Coupon>()
            .Include(c => c.Sellers)
            .FirstOrDefaultAsync(c => c.PublicId == command.CouponId, cancellationToken);

        if (coupon is null || (command.SellerId is { } seller && coupon.SellerId != seller))
        {
            return Result.Failure<CouponDto>(PromotionErrors.CouponNotFound);
        }

        coupon.End();

        return await CouponSave.SaveAsync(dbContext, coupon, command.SellerId, cancellationToken);
    }
}

/// <summary>
/// Coupons, newest first: every one for staff, or a seller's own. At most the latest 200 - older
/// ones have long ended.
/// </summary>
public sealed record ListCouponsQuery(Guid? SellerId) : IQuery<IReadOnlyList<CouponDto>>;

internal sealed class ListCouponsQueryHandler(UPBazaarDbContext dbContext) : IQueryHandler<ListCouponsQuery, IReadOnlyList<CouponDto>>
{
    public async Task<Result<IReadOnlyList<CouponDto>>> HandleAsync(ListCouponsQuery query, CancellationToken cancellationToken)
    {
        var coupons = dbContext.Set<Coupon>().AsNoTracking().Include(c => c.Sellers).AsQueryable();

        if (query.SellerId is { } seller)
        {
            coupons = coupons.Where(c => c.SellerId == seller);
        }

        var list = await coupons.OrderByDescending(c => c.Id).Take(200).ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<CouponDto>>([.. list.Select(c => c.ToDto(query.SellerId))]);
    }
}

/// <summary>Platform campaigns a seller can join - still running, with sellers paying - and whether they have.</summary>
public sealed record ListCampaignsQuery(Guid SellerId) : IQuery<IReadOnlyList<CouponDto>>;

internal sealed class ListCampaignsQueryHandler(UPBazaarDbContext dbContext, IClock clock)
    : IQueryHandler<ListCampaignsQuery, IReadOnlyList<CouponDto>>
{
    public async Task<Result<IReadOnlyList<CouponDto>>> HandleAsync(ListCampaignsQuery query, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var campaigns = await dbContext.Set<Coupon>()
            .AsNoTracking()
            .Include(c => c.Sellers)
            .Where(c => c.SellerId == null && c.FundedBy == CouponFunding.Seller && c.IsActive && (c.EndsAtUtc == null || c.EndsAtUtc > now))
            .OrderByDescending(c => c.Id)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<CouponDto>>([.. campaigns.Select(c => c.ToDto(query.SellerId))]);
    }
}

/// <summary>A seller joins or leaves a platform campaign.</summary>
public sealed record SetCampaignJoinedCommand(Guid CouponId, Guid SellerId, bool Joined) : ICommand<CouponDto>;

internal sealed class SetCampaignJoinedCommandHandler(UPBazaarDbContext dbContext, IClock clock)
    : ICommandHandler<SetCampaignJoinedCommand, CouponDto>
{
    public async Task<Result<CouponDto>> HandleAsync(SetCampaignJoinedCommand command, CancellationToken cancellationToken)
    {
        var coupon = await dbContext.Set<Coupon>()
            .Include(c => c.Sellers)
            .FirstOrDefaultAsync(c => c.PublicId == command.CouponId, cancellationToken);

        if (coupon is null)
        {
            return Result.Failure<CouponDto>(PromotionErrors.CouponNotFound);
        }

        var changed = command.Joined ? coupon.Join(command.SellerId, clock.UtcNow) : coupon.Leave(command.SellerId);

        return changed.IsFailure
            ? Result.Failure<CouponDto>(changed.Error)
            : await CouponSave.SaveAsync(dbContext, coupon, command.SellerId, cancellationToken);
    }
}

internal static class CouponSave
{
    public static async Task<Result<CouponDto>> SaveAsync(
        UPBazaarDbContext dbContext,
        Coupon coupon,
        Guid? viewer,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<CouponDto>(PromotionErrors.ConcurrentChange);
        }

        return coupon.ToDto(viewer);
    }
}

internal static class CouponMappings
{
    /// <param name="coupon">The coupon; its sellers must be loaded.</param>
    /// <param name="viewer">The seller looking, if a seller is: says whether they joined.</param>
    public static CouponDto ToDto(this Coupon coupon, Guid? viewer) => new(
        coupon.PublicId,
        coupon.Code,
        coupon.Description,
        coupon.SellerId,
        coupon.FundedBy.ToString(),
        coupon.DiscountType.ToString(),
        coupon.Value,
        coupon.MaxDiscount,
        coupon.MinOrderValue,
        coupon.StartsAtUtc,
        coupon.EndsAtUtc,
        coupon.TotalLimit,
        coupon.PerBuyerLimit,
        coupon.Uses,
        coupon.IsActive,
        coupon.Sellers.Count,
        viewer is { } seller && coupon.Sellers.Any(s => s.SellerId == seller));
}
