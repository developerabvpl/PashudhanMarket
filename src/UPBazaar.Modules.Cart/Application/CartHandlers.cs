using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Cart.Contracts.Dtos;
using UPBazaar.Modules.Cart.Domain;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Cart.Application;

/// <summary>The caller's own cart.</summary>
public sealed record GetCartQuery(Guid BuyerId) : IQuery<CartDto>;

internal sealed class GetCartQueryHandler(CartReader reader) : IQueryHandler<GetCartQuery, CartDto>
{
    public async Task<Result<CartDto>> HandleAsync(GetCartQuery query, CancellationToken cancellationToken) =>
        await reader.ToDtoAsync(await reader.FindAsync(query.BuyerId, cancellationToken), cancellationToken);
}

/// <summary>Sets how many of one product the buyer wants. Zero removes it.</summary>
public sealed record SetCartItemCommand(Guid BuyerId, Guid ProductId, int Quantity) : ICommand<CartDto>;

internal sealed class SetCartItemCommandValidator : AbstractValidator<SetCartItemCommand>
{
    public SetCartItemCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).InclusiveBetween(0, ShoppingCart.MaxQuantityPerLine);
    }
}

/// <summary>
/// Refuses what could not be bought: a product that is not on sale, or more than is in stock.
/// Lowering a quantity is always allowed, even when stock has since fallen below it - that is
/// exactly what a buyer does to fix an InsufficientStock line.
/// </summary>
internal sealed class SetCartItemCommandHandler(CartReader reader, CartWriter writer)
    : ICommandHandler<SetCartItemCommand, CartDto>
{
    public async Task<Result<CartDto>> HandleAsync(SetCartItemCommand command, CancellationToken cancellationToken)
    {
        var cart = await reader.FindOrCreateAsync(command.BuyerId, cancellationToken);
        var current = cart.Lines.FirstOrDefault(l => l.ProductId == command.ProductId)?.Quantity ?? 0;

        decimal price = 0;

        if (command.Quantity > 0)
        {
            var (products, stock) = await reader.LookUpAsync([command.ProductId], cancellationToken);

            if (!products.TryGetValue(command.ProductId, out var product) || !product.IsPurchasable)
            {
                return Result.Failure<CartDto>(CartErrors.ProductNotOnSale);
            }

            if (command.Quantity > current && command.Quantity > stock[command.ProductId].AvailableQuantity)
            {
                return Result.Failure<CartDto>(CartErrors.InsufficientStock);
            }

            price = product.Price;
        }

        var result = cart.SetQuantity(command.ProductId, command.Quantity, price);

        return result.IsFailure
            ? Result.Failure<CartDto>(result.Error)
            : await writer.SaveAsync(cart, cancellationToken);
    }
}

/// <summary>Empties the caller's cart.</summary>
public sealed record ClearCartCommand(Guid BuyerId) : ICommand<CartDto>;

internal sealed class ClearCartCommandHandler(CartReader reader, CartWriter writer)
    : ICommandHandler<ClearCartCommand, CartDto>
{
    public async Task<Result<CartDto>> HandleAsync(ClearCartCommand command, CancellationToken cancellationToken)
    {
        var cart = await reader.FindAsync(command.BuyerId, cancellationToken);

        if (cart is null)
        {
            return await reader.ToDtoAsync(null, cancellationToken);
        }

        cart.Clear();

        return await writer.SaveAsync(cart, cancellationToken);
    }
}

/// <summary>Accepts every current price, clearing the PriceChanged flags.</summary>
public sealed record AcknowledgeCartPricesCommand(Guid BuyerId) : ICommand<CartDto>;

internal sealed class AcknowledgeCartPricesCommandHandler(CartReader reader, CartWriter writer)
    : ICommandHandler<AcknowledgeCartPricesCommand, CartDto>
{
    public async Task<Result<CartDto>> HandleAsync(
        AcknowledgeCartPricesCommand command,
        CancellationToken cancellationToken)
    {
        var cart = await reader.FindAsync(command.BuyerId, cancellationToken);

        if (cart is null)
        {
            return await reader.ToDtoAsync(null, cancellationToken);
        }

        var (products, _) = await reader.LookUpAsync([.. cart.Lines.Select(l => l.ProductId)], cancellationToken);

        cart.AcknowledgePrices(products.Values.Where(p => p.IsPurchasable).ToDictionary(p => p.Id, p => p.Price));

        return await writer.SaveAsync(cart, cancellationToken);
    }
}

/// <summary>
/// Folds the basket a buyer filled before signing in into their saved cart.
///
/// Forgiving by design, because the buyer did nothing wrong: quantities add up and are capped,
/// products no longer on sale are skipped and reported rather than failing the merge, and stock
/// is not checked here - a short line shows up flagged in the cart, where the buyer can fix it.
/// </summary>
public sealed record MergeCartCommand(Guid BuyerId, IReadOnlyList<MergeLine> Lines) : ICommand<CartMergeResultDto>;

/// <summary>One line of the guest basket.</summary>
public sealed record MergeLine(Guid ProductId, int Quantity);

internal sealed class MergeCartCommandValidator : AbstractValidator<MergeCartCommand>
{
    public MergeCartCommandValidator()
    {
        RuleFor(x => x.Lines).NotNull();
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(ShoppingCart.MaxLines).OverridePropertyName("Lines");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });
    }
}

internal sealed class MergeCartCommandHandler(CartReader reader, CartWriter writer)
    : ICommandHandler<MergeCartCommand, CartMergeResultDto>
{
    public async Task<Result<CartMergeResultDto>> HandleAsync(
        MergeCartCommand command,
        CancellationToken cancellationToken)
    {
        // A basket assembled offline can list the same product twice; add those together first.
        var wanted = command.Lines
            .GroupBy(l => l.ProductId)
            .Select(g => (ProductId: g.Key, Quantity: g.Sum(l => l.Quantity)))
            .ToList();

        var cart = await reader.FindOrCreateAsync(command.BuyerId, cancellationToken);
        var (products, _) = await reader.LookUpAsync([.. wanted.Select(w => w.ProductId)], cancellationToken);
        var skipped = new List<Guid>();

        foreach (var (productId, quantity) in wanted)
        {
            if (!products.TryGetValue(productId, out var product)
                || !product.IsPurchasable
                || !cart.Merge(productId, quantity, product.Price))
            {
                skipped.Add(productId);
            }
        }

        var saved = await writer.SaveAsync(cart, cancellationToken);

        return saved.IsFailure
            ? Result.Failure<CartMergeResultDto>(saved.Error)
            : new CartMergeResultDto(saved.Value, skipped);
    }
}

/// <summary>Saves a cart and returns it as the buyer now sees it.</summary>
internal sealed class CartWriter(UPBazaarDbContext dbContext, CartReader reader)
{
    public async Task<Result<CartDto>> SaveAsync(ShoppingCart cart, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<CartDto>(CartErrors.ConcurrentChange);
        }
        catch (DbUpdateException) when (dbContext.Entry(cart).State == EntityState.Added)
        {
            // Two first-ever adds from the same buyer raced to create the cart, and the unique
            // index on BuyerId let only one through. The buyer's retry will find it.
            return Result.Failure<CartDto>(CartErrors.ConcurrentChange);
        }

        return await reader.ToDtoAsync(cart, cancellationToken);
    }
}
