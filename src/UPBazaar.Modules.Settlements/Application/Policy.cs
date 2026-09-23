using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.Modules.Settlements.Contracts.Dtos;
using UPBazaar.Modules.Settlements.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Settlements.Application;

/// <summary>The rates new earnings are worked out at.</summary>
public sealed record GetSettlementPolicyQuery : IQuery<SettlementPolicyDto>;

internal sealed class GetSettlementPolicyQueryHandler(PolicyReader policy)
    : IQueryHandler<GetSettlementPolicyQuery, SettlementPolicyDto>
{
    public async Task<Result<SettlementPolicyDto>> HandleAsync(GetSettlementPolicyQuery query, CancellationToken cancellationToken) =>
        (await policy.GetAsync(cancellationToken)).ToDto();
}

/// <summary>Changes the default commission and the tax rates, for sales delivered from now on.</summary>
public sealed record UpdateSettlementPolicyCommand(decimal DefaultCommissionPercent, decimal TcsPercent, decimal TdsPercent)
    : ICommand<SettlementPolicyDto>;

internal sealed class UpdateSettlementPolicyCommandValidator : AbstractValidator<UpdateSettlementPolicyCommand>
{
    public UpdateSettlementPolicyCommandValidator()
    {
        RuleFor(x => x.DefaultCommissionPercent).InclusiveBetween(0m, 100m).Must(Rates.HasTwoDecimals);
        RuleFor(x => x.TcsPercent).InclusiveBetween(0m, 100m).Must(Rates.HasTwoDecimals);
        RuleFor(x => x.TdsPercent).InclusiveBetween(0m, 100m).Must(Rates.HasTwoDecimals);
    }
}

/// <summary>
/// Refuses taxes that, added to any seller's own commission, would leave that seller nothing:
/// the rates are checked together, not each on its own.
/// </summary>
internal sealed class UpdateSettlementPolicyCommandHandler(UPBazaarDbContext dbContext, PolicyReader reader)
    : ICommandHandler<UpdateSettlementPolicyCommand, SettlementPolicyDto>
{
    public async Task<Result<SettlementPolicyDto>> HandleAsync(UpdateSettlementPolicyCommand command, CancellationToken cancellationToken)
    {
        var highestOwn = await dbContext.Set<SellerCommission>()
            .MaxAsync(c => (decimal?)c.CommissionPercent, cancellationToken) ?? 0m;

        if (highestOwn + command.TcsPercent + command.TdsPercent >= 100m)
        {
            return Result.Failure<SettlementPolicyDto>(SettlementErrors.RatesTooHigh);
        }

        var policy = await reader.GetAsync(cancellationToken);
        var changed = policy.Change(command.DefaultCommissionPercent, command.TcsPercent, command.TdsPercent);

        if (changed.IsFailure)
        {
            return Result.Failure<SettlementPolicyDto>(changed.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return policy.ToDto();
    }
}

/// <summary>Every seller with a commission of their own.</summary>
public sealed record ListSellerCommissionsQuery : IQuery<IReadOnlyList<SellerCommissionDto>>;

internal sealed class ListSellerCommissionsQueryHandler(UPBazaarDbContext dbContext, ISellerDirectory sellers)
    : IQueryHandler<ListSellerCommissionsQuery, IReadOnlyList<SellerCommissionDto>>
{
    public async Task<Result<IReadOnlyList<SellerCommissionDto>>> HandleAsync(
        ListSellerCommissionsQuery query,
        CancellationToken cancellationToken)
    {
        var commissions = await dbContext.Set<SellerCommission>().AsNoTracking().ToListAsync(cancellationToken);
        var names = await sellers.GetShopNamesAsync([.. commissions.Select(c => c.SellerId)], cancellationToken);

        return Result.Success<IReadOnlyList<SellerCommissionDto>>(
        [
            .. commissions
                .Select(c => new SellerCommissionDto(c.SellerId, names.GetValueOrDefault(c.SellerId), c.CommissionPercent))
                .OrderBy(c => c.ShopName)
        ]);
    }
}

/// <summary>Gives a seller their own commission, or changes it; null goes back to the default.</summary>
public sealed record SetSellerCommissionCommand(Guid SellerId, decimal? CommissionPercent) : ICommand<SellerCommissionDto?>;

internal sealed class SetSellerCommissionCommandValidator : AbstractValidator<SetSellerCommissionCommand>
{
    public SetSellerCommissionCommandValidator()
    {
        RuleFor(x => x.SellerId).NotEmpty();
        RuleFor(x => x.CommissionPercent!.Value)
            .InclusiveBetween(0m, 100m)
            .Must(Rates.HasTwoDecimals)
            .OverridePropertyName("CommissionPercent")
            .When(x => x.CommissionPercent is not null);
    }
}

internal sealed class SetSellerCommissionCommandHandler(UPBazaarDbContext dbContext, PolicyReader reader, ISellerDirectory sellers)
    : ICommandHandler<SetSellerCommissionCommand, SellerCommissionDto?>
{
    public async Task<Result<SellerCommissionDto?>> HandleAsync(SetSellerCommissionCommand command, CancellationToken cancellationToken)
    {
        var names = await sellers.GetShopNamesAsync([command.SellerId], cancellationToken);

        if (!names.TryGetValue(command.SellerId, out var shopName))
        {
            return Result.Failure<SellerCommissionDto?>(SettlementErrors.SellerNotFound);
        }

        var existing = await dbContext.Set<SellerCommission>()
            .FirstOrDefaultAsync(c => c.SellerId == command.SellerId, cancellationToken);

        if (command.CommissionPercent is not { } percent)
        {
            if (existing is not null)
            {
                dbContext.Set<SellerCommission>().Remove(existing);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Result.Success<SellerCommissionDto?>(null);
        }

        var policy = await reader.GetAsync(cancellationToken);

        if (percent + policy.TcsPercent + policy.TdsPercent >= 100m)
        {
            return Result.Failure<SellerCommissionDto?>(SettlementErrors.RatesTooHigh);
        }

        if (existing is null)
        {
            dbContext.Set<SellerCommission>().Add(SellerCommission.Create(command.SellerId, percent));
        }
        else
        {
            existing.Change(percent);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new SellerCommissionDto(command.SellerId, shopName, percent);
    }
}

/// <summary>Checks shared by every rate.</summary>
internal static class Rates
{
    /// <summary>Hundredths of a percent are as fine as any agreement goes.</summary>
    public static bool HasTwoDecimals(decimal percent) => decimal.Round(percent, 2) == percent;
}
