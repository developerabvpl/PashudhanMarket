using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Catalog.Contracts;
using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.Modules.Inventory.Domain;
using UPBazaar.Modules.Inventory.Services;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Inventory.Application;

/// <summary>One product's stock and its latest movements.</summary>
public sealed record GetStockQuery(Guid ProductId) : IQuery<StockDetailDto>;

internal sealed class GetStockQueryHandler(UPBazaarDbContext dbContext, IProductCatalog catalog)
    : IQueryHandler<GetStockQuery, StockDetailDto>
{
    /// <summary>Enough history to explain the current figure without paging.</summary>
    private const int RecentMovements = 50;

    public async Task<Result<StockDetailDto>> HandleAsync(GetStockQuery query, CancellationToken cancellationToken)
    {
        var item = await dbContext.Set<StockItem>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProductId == query.ProductId, cancellationToken);

        if (item is null)
        {
            // Never counted is a real state - zero, no history - but only for a product that exists.
            return await catalog.ProductExistsAsync(query.ProductId, cancellationToken)
                ? new StockDetailDto(StockLevelDto.None(query.ProductId), [])
                : Result.Failure<StockDetailDto>(InventoryErrors.ProductNotFound);
        }

        var movements = await dbContext.Set<StockMovement>()
            .AsNoTracking()
            .Where(m => m.StockItemId == item.Id)
            .OrderByDescending(m => m.OccurredAtUtc)
            .ThenByDescending(m => m.Id)
            .Take(RecentMovements)
            .ToListAsync(cancellationToken);

        return new StockDetailDto(item.ToDto(), [.. movements.Select(m => m.ToDto())]);
    }
}

/// <summary>
/// Stock levels, lowest availability first, for a restocking screen. Products Inventory has never
/// counted do not appear: they have no row, and inventing one per catalogue product is Catalog's
/// business to ask for, not Inventory's to guess.
/// </summary>
public sealed record ListStockQuery(int Page, int PageSize, int? MaxAvailable) : IQuery<PagedList<StockLevelDto>>;

internal sealed class ListStockQueryValidator : AbstractValidator<ListStockQuery>
{
    public ListStockQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.MaxAvailable).GreaterThanOrEqualTo(0);
    }
}

internal sealed class ListStockQueryHandler(UPBazaarDbContext dbContext)
    : IQueryHandler<ListStockQuery, PagedList<StockLevelDto>>
{
    public async Task<Result<PagedList<StockLevelDto>>> HandleAsync(
        ListStockQuery query,
        CancellationToken cancellationToken)
    {
        var items = dbContext.Set<StockItem>().AsNoTracking();

        if (query.MaxAvailable is { } max)
        {
            items = items.Where(s => s.OnHandQuantity - s.ReservedQuantity <= max);
        }

        var totalCount = await items.CountAsync(cancellationToken);

        var page = await items
            .OrderBy(s => s.OnHandQuantity - s.ReservedQuantity)
            .ThenBy(s => s.ProductId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new StockLevelDto(
                s.ProductId,
                s.OnHandQuantity,
                s.ReservedQuantity,
                s.OnHandQuantity - s.ReservedQuantity))
            .ToListAsync(cancellationToken);

        return new PagedList<StockLevelDto>(page, query.Page, query.PageSize, totalCount);
    }
}

/// <summary>A delivery arrived.</summary>
public sealed record ReceiveStockCommand(Guid ProductId, int Quantity, string? Reason, string? Reference)
    : ICommand<StockLevelDto>;

internal sealed class ReceiveStockCommandValidator : AbstractValidator<ReceiveStockCommand>
{
    public ReceiveStockCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).InclusiveBetween(1, StockRules.MaxQuantity);
        RuleFor(x => x.Reason).MaximumLength(512);
        RuleFor(x => x.Reference).MaximumLength(128);
    }
}

internal sealed class ReceiveStockCommandHandler(StockWriter writer)
    : ICommandHandler<ReceiveStockCommand, StockLevelDto>
{
    public Task<Result<StockLevelDto>> HandleAsync(ReceiveStockCommand command, CancellationToken cancellationToken) =>
        writer.ApplyAsync(
            command.ProductId,
            createIfMissing: true,
            (item, now, actor) => item.Receive(command.Quantity, command.Reason, command.Reference, now, actor),
            cancellationToken);
}

/// <summary>A stock-take found this many on the shelf.</summary>
public sealed record CountStockCommand(Guid ProductId, int CountedOnHand, string? Reason) : ICommand<StockLevelDto>;

internal sealed class CountStockCommandValidator : AbstractValidator<CountStockCommand>
{
    public CountStockCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.CountedOnHand).InclusiveBetween(0, StockRules.MaxQuantity);
        RuleFor(x => x.Reason).MaximumLength(512);
    }
}

internal sealed class CountStockCommandHandler(StockWriter writer)
    : ICommandHandler<CountStockCommand, StockLevelDto>
{
    public Task<Result<StockLevelDto>> HandleAsync(CountStockCommand command, CancellationToken cancellationToken) =>
        writer.ApplyAsync(
            command.ProductId,
            createIfMissing: true,
            (item, now, actor) => item.Count(command.CountedOnHand, command.Reason, now, actor),
            cancellationToken);
}

/// <summary>Stock was lost, damaged or expired.</summary>
public sealed record WriteOffStockCommand(Guid ProductId, int Quantity, string Reason) : ICommand<StockLevelDto>;

internal sealed class WriteOffStockCommandValidator : AbstractValidator<WriteOffStockCommand>
{
    public WriteOffStockCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).InclusiveBetween(1, StockRules.MaxQuantity);

        // A write-off with no reason is the one ledger line nobody can later explain.
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(512)
            .WithMessage("Say why the stock is being written off.");
    }
}

internal sealed class WriteOffStockCommandHandler(StockWriter writer)
    : ICommandHandler<WriteOffStockCommand, StockLevelDto>
{
    public Task<Result<StockLevelDto>> HandleAsync(WriteOffStockCommand command, CancellationToken cancellationToken) =>
        writer.ApplyAsync(
            command.ProductId,
            createIfMissing: false,
            (item, now, actor) => item.WriteOff(command.Quantity, command.Reason.Trim(), now, actor),
            cancellationToken);
}

internal static class StockRules
{
    /// <summary>A sanity bound: nobody receives a million diyas in one delivery.</summary>
    public const int MaxQuantity = 1_000_000;
}

/// <summary>
/// The load-change-save shape every staff stock change shares, with its three ways to fail: the
/// product does not exist, the change breaks a rule, or another request changed the row first.
/// </summary>
internal sealed class StockWriter(
    UPBazaarDbContext dbContext,
    IProductCatalog catalog,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<Result<StockLevelDto>> ApplyAsync(
        Guid productId,
        bool createIfMissing,
        Func<StockItem, DateTime, string?, Result> change,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.Set<StockItem>()
            .FirstOrDefaultAsync(s => s.ProductId == productId, cancellationToken);

        if (item is null)
        {
            if (!await catalog.ProductExistsAsync(productId, cancellationToken))
            {
                return Result.Failure<StockLevelDto>(InventoryErrors.ProductNotFound);
            }

            if (!createIfMissing)
            {
                // Nothing was ever counted, so there is nothing to write off.
                return Result.Failure<StockLevelDto>(InventoryErrors.InsufficientStock);
            }

            item = StockItem.Create(productId);
            dbContext.Set<StockItem>().Add(item);
        }

        var result = change(item, clock.UtcNow, currentUser.UserId);

        if (result.IsFailure)
        {
            return Result.Failure<StockLevelDto>(result.Error);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Unlike a checkout, a person is waiting on this one: tell them, rather than applying
            // their correction on top of a figure they have not seen.
            return Result.Failure<StockLevelDto>(InventoryErrors.ConcurrentChange);
        }

        return item.ToDto();
    }
}
