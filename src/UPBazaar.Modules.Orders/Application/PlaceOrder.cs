using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Cart.Contracts;
using UPBazaar.Modules.Catalog.Contracts;
using UPBazaar.Modules.Inventory.Contracts;
using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.Modules.Orders.Contracts.Dtos;
using UPBazaar.Modules.Orders.Domain;
using UPBazaar.Modules.Promotions.Contracts;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Orders.Application;

/// <summary>Turns the buyer's cart into an order.</summary>
/// <param name="BuyerId">Who is buying; always the caller.</param>
/// <param name="PaymentMethod">CashOnDelivery or Online.</param>
/// <param name="Address">Where it goes.</param>
/// <param name="CouponCode">A coupon code to use, if the buyer entered one.</param>
public sealed record PlaceOrderCommand(Guid BuyerId, string PaymentMethod, DeliveryAddressDto Address, string? CouponCode = null)
    : ICommand<OrderDto>;

internal sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(x => x.BuyerId).NotEmpty();

        RuleFor(x => x.PaymentMethod)
            .Must(m => Enum.TryParse<PaymentMethod>(m, ignoreCase: true, out _))
            .WithMessage("Payment method must be CashOnDelivery or Online.");

        RuleFor(x => x.Address).NotNull().SetValidator(new DeliveryAddressValidator());
        RuleFor(x => x.CouponCode).MaximumLength(20);
    }
}

/// <summary>
/// What a courier needs to find the door. Strict about the parts couriers reject shipments
/// over - mobile, PIN code, state - and lenient about the free text.
/// </summary>
internal sealed class DeliveryAddressValidator : AbstractValidator<DeliveryAddressDto>
{
    public DeliveryAddressValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(DeliveryAddress.NameMaxLength);

        RuleFor(x => x.Mobile)
            .NotEmpty()
            .Matches("^[6-9][0-9]{9}$")
            .WithMessage("Enter a 10-digit mobile number.");

        RuleFor(x => x.Line1).NotEmpty().MaximumLength(DeliveryAddress.LineMaxLength);
        RuleFor(x => x.Line2).MaximumLength(DeliveryAddress.LineMaxLength);
        RuleFor(x => x.Landmark).MaximumLength(DeliveryAddress.LineMaxLength);
        RuleFor(x => x.City).NotEmpty().MaximumLength(DeliveryAddress.PlaceMaxLength);
        RuleFor(x => x.District).MaximumLength(DeliveryAddress.PlaceMaxLength);

        RuleFor(x => x.State)
            .Must(s => IndianStates.Canonical(s) is not null)
            .WithMessage("Choose a state or union territory.");

        // No Indian PIN code starts with 0.
        RuleFor(x => x.Pincode)
            .NotEmpty()
            .Matches("^[1-9][0-9]{5}$")
            .WithMessage("Enter a 6-digit PIN code.");
    }
}

/// <summary>
/// Checkout, in one transaction: take the cart's lines at the prices the buyer was shown, take off
/// any coupon, add the delivery charge, hold the stock, write the order, empty the cart. For cash
/// on delivery the held stock is committed there and then, because the order is confirmed; an
/// online order keeps it held until the payment arrives or the payment window closes.
///
/// A cart with any problem - a changed price, a withdrawn product, too little stock - is refused
/// by Cart before anything happens, so an order is never placed for something the buyer was not
/// shown. Two tabs checking out at once cannot both win: a per-buyer lock makes the second wait
/// for the first, and it then finds the cart empty.
/// </summary>
internal sealed class PlaceOrderCommandHandler(
    UPBazaarDbContext dbContext,
    OrderTransaction transaction,
    ICartService cart,
    IProductCatalog catalog,
    IInventoryService inventory,
    ICouponPricing coupons,
    IOptions<OrdersModuleOptions> options,
    IClock clock) : ICommandHandler<PlaceOrderCommand, OrderDto>
{
    /// <summary>
    /// The stock hold outlasts the payment window by a few minutes, so an order is always
    /// cancelled by its own deadline - and its buyer told why - before Inventory quietly lets the
    /// stock go underneath it.
    /// </summary>
    private static readonly TimeSpan OnlineHold = Order.PaymentWindow + TimeSpan.FromMinutes(5);

    /// <summary>Cash on delivery commits in the same breath; the hold only has to survive this request.</summary>
    private static readonly TimeSpan CodHold = TimeSpan.FromMinutes(5);

    public Task<Result<OrderDto>> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken) =>
        transaction.RunAsync(ct => PlaceAsync(command, ct), cancellationToken);

    private async Task<Result<OrderDto>> PlaceAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var method = Enum.Parse<PaymentMethod>(command.PaymentMethod, ignoreCase: true);

        await LockCheckoutAsync(command.BuyerId, cancellationToken);

        var checkout = await cart.GetCheckoutLinesAsync(command.BuyerId, cancellationToken);

        if (checkout.IsFailure)
        {
            return Result.Failure<OrderDto>(checkout.Error);
        }

        var lines = checkout.Value;

        if (lines.Select(l => l.Currency).Distinct().Count() > 1)
        {
            return Result.Failure<OrderDto>(OrderErrors.MixedCurrencies);
        }

        var couponLines = lines.Select(l => new CouponLineDto(l.ProductId, l.SellerId, l.UnitPrice * l.Quantity)).ToList();
        CouponDiscountDto? coupon = null;

        if (!string.IsNullOrWhiteSpace(command.CouponCode))
        {
            var quoted = await coupons.QuoteAsync(command.CouponCode, command.BuyerId, couponLines, cancellationToken);

            if (quoted.IsFailure)
            {
                return Result.Failure<OrderDto>(quoted.Error);
            }

            coupon = quoted.Value;
        }

        var discounts = coupon?.Lines.ToDictionary(l => l.ProductId, l => l.Discount) ?? [];

        // Judged on the goods before the coupon, so money off never costs the buyer delivery.
        var deliveryFee = options.Value.DeliveryFeeFor(lines.Sum(l => l.UnitPrice * l.Quantity));
        HashSet<Guid>? freeDeliveryFor = null;

        if (coupon is { FreeDelivery: true })
        {
            // Used up for nothing is worse than refused: say delivery is free already.
            if (deliveryFee == 0m)
            {
                return Result.Failure<OrderDto>(OrderErrors.DeliveryAlreadyFree);
            }

            var covered = coupon.Lines.Select(l => l.ProductId).ToHashSet();
            freeDeliveryFor = [.. lines.Where(l => covered.Contains(l.ProductId)).Select(l => l.SellerId)];
        }

        // The cart hands over ids, quantities and prices; the name and SKU to freeze into the
        // order come from the catalogue. Cart has just checked every product is on sale.
        var products = await catalog.GetProductsAsync([.. lines.Select(l => l.ProductId)], cancellationToken);
        var number = await NewNumberAsync(cancellationToken);

        var reservation = await inventory.ReserveAsync(
            number,
            [.. lines.Select(l => new ReservationLineDto(l.ProductId, l.Quantity))],
            method == PaymentMethod.CashOnDelivery ? CodHold : OnlineHold,
            cancellationToken);

        if (reservation.IsFailure)
        {
            // Short stock here means the cart said there was enough a moment ago and somebody
            // else's checkout got there first; say so in the buyer's terms.
            return Result.Failure<OrderDto>(
                reservation.Error.Code == "inventory.stock.insufficient" ? OrderErrors.OutOfStock : reservation.Error);
        }

        if (method == PaymentMethod.CashOnDelivery)
        {
            var committed = await inventory.CommitAsync(reservation.Value, cancellationToken);

            if (committed.IsFailure)
            {
                return Result.Failure<OrderDto>(committed.Error);
            }
        }

        var address = command.Address;
        var order = Order.Place(
            number,
            command.BuyerId,
            method,
            DeliveryAddress.Create(
                address.FullName,
                address.Mobile,
                address.Line1,
                address.Line2,
                address.Landmark,
                address.City,
                address.District,
                address.State,
                address.Pincode),
            lines[0].Currency,
            [.. lines.Select(l => new OrderLineInput(
                l.SellerId,
                l.ProductId,
                products[l.ProductId].Sku,
                products[l.ProductId].Name,
                l.UnitPrice,
                l.Quantity,
                discounts.GetValueOrDefault(l.ProductId)))],

            deliveryFee,
            coupon is null ? null : (coupon.Code, coupon.FundedBy),
            reservation.Value,
            clock.UtcNow,
            freeDeliveryFor);

        dbContext.Set<Order>().Add(order);

        if (coupon is not null)
        {
            // Used, and checked once more, in this transaction: a coupon that ran out or changed
            // since it was priced fails the checkout, rather than the order going through on a
            // discount it no longer has.
            var redeemed = await coupons.RedeemAsync(coupon.Code, command.BuyerId, order.PublicId, couponLines, cancellationToken);

            if (redeemed.IsFailure)
            {
                return Result.Failure<OrderDto>(redeemed.Error);
            }

            if (redeemed.Value.Discount != coupon.Discount)
            {
                return Result.Failure<OrderDto>(OrderErrors.ConcurrentChange);
            }
        }

        await cart.StageClearAsync(command.BuyerId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return order.ToDto();
    }

    /// <summary>
    /// One checkout per buyer at a time, held until this transaction ends.
    ///
    /// Row versions alone do not stop a double submit. When Inventory retries a reservation after
    /// a stock conflict it clears the change tracker, so the second checkout re-reads a cart the
    /// first has already emptied, clears nothing, and would place a second order for the same
    /// goods. With the lock, the second waits for the first to finish and then finds the cart
    /// empty. A SQL Server application lock rather than a row lock because there may be no cart
    /// row to lock, and it names exactly what is being serialised.
    /// </summary>
    private async Task LockCheckoutAsync(Guid buyerId, CancellationToken cancellationToken)
    {
        var resource = $"orders.checkout:{buyerId:N}";

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51000, 'Timed out waiting for another checkout by the same buyer.', 1;
            """,
            cancellationToken);
    }

    private async Task<string> NewNumberAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var number = OrderNumber.New(clock.UtcNow);

            if (!await dbContext.Set<Order>().AnyAsync(o => o.Number == number, cancellationToken))
            {
                return number;
            }
        }
    }
}
