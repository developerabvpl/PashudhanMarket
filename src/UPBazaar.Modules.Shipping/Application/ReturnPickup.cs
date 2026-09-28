using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Orders.Contracts.Events;
using UPBazaar.Modules.Sellers.Contracts;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Application;

/// <summary>
/// Books a courier to collect a buyer's approved return and take it to the seller.
/// </summary>
/// <param name="OrderId">The order.</param>
/// <param name="PartId">The part going back.</param>
/// <param name="SellerId">Set when the seller asks: the part must be theirs. Null for staff and the system.</param>
public sealed record BookReturnPickupCommand(Guid OrderId, Guid PartId, Guid? SellerId) : ICommand<ShipmentDto>;

internal sealed class BookReturnPickupCommandValidator : AbstractValidator<BookReturnPickupCommand>
{
    public BookReturnPickupCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.PartId).NotEmpty();
    }
}

internal sealed class BookReturnPickupCommandHandler(ReturnPickupBooker booker)
    : ICommandHandler<BookReturnPickupCommand, ShipmentDto>
{
    public Task<Result<ShipmentDto>> HandleAsync(BookReturnPickupCommand command, CancellationToken cancellationToken) =>
        booker.BookAsync(command.OrderId, command.PartId, command.SellerId, cancellationToken);
}

/// <summary>
/// A return was approved: book its pickup straight away. A booking that fails is left in Booking
/// with the error on it rather than retried by the outbox, since a courier that cannot serve the
/// buyer's PIN code will not start to on the next attempt; staff or the seller book it again
/// once the cause is fixed, and the booking resumes from the step that failed.
/// </summary>
internal sealed partial class OrderPartReturnApprovedHandler(
    ReturnPickupBooker booker,
    ILogger<OrderPartReturnApprovedHandler> logger) : IDomainEventHandler<OrderPartReturnApprovedDomainEvent>
{
    public async Task HandleAsync(OrderPartReturnApprovedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var booked = await booker.BookAsync(domainEvent.OrderId, domainEvent.PartId, sellerId: null, cancellationToken);

        if (booked.IsFailure)
        {
            LogNotBooked(logger, domainEvent.Number, booked.Error.Code);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Return pickup for order {Number} was not booked: {ErrorCode}")]
    private static partial void LogNotBooked(ILogger logger, string number, string errorCode);
}

/// <summary>
/// Books a return in the carrier's two steps - return order, then AWB - saving after each, so
/// booking again resumes where it stopped. The carrier schedules the collection itself once an AWB
/// is issued, so there is no separate pickup request as there is on the way out.
///
/// The box is the one it went out in when there was an outbound shipment, or else worked out from
/// the products' packages as at packing.
/// </summary>
internal sealed class ReturnPickupBooker(
    UPBazaarDbContext dbContext,
    IOrderFulfilmentService orders,
    ISellerDirectory sellers,
    ParcelPlanner planner,
    ICourierGateway courier,
    IClock clock)
{
    public async Task<Result<ShipmentDto>> BookAsync(
        Guid orderId,
        Guid partId,
        Guid? sellerId,
        CancellationToken cancellationToken)
    {
        if (!courier.IsEnabled)
        {
            return Result.Failure<ShipmentDto>(ShippingErrors.CourierDisabled);
        }

        var found = await orders.GetPartAsync(orderId, partId, cancellationToken);

        if (found.IsFailure || (sellerId is { } seller && found.Value.SellerId != seller))
        {
            return Result.Failure<ShipmentDto>(ShippingErrors.PartNotFound);
        }

        var part = found.Value;

        var shipment = await dbContext.Set<Shipment>()
            .Include(s => s.Events)
            .Include(s => s.Charges)
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                s => s.OrderPartId == part.PartId && s.Direction == ShipmentDirection.Return && s.Status != ShipmentStatus.Cancelled,
                cancellationToken);

        if (shipment is not null && shipment.Status != ShipmentStatus.Booking)
        {
            return shipment.ToDto(courier);
        }

        if (!part.BuyerReturnApproved || part.Status != "Returning")
        {
            return Result.Failure<ShipmentDto>(ShippingErrors.NoApprovedReturn);
        }

        var returnTo = await sellers.GetReturnAddressAsync(part.SellerId, cancellationToken);

        if (returnTo is null)
        {
            return Result.Failure<ShipmentDto>(ShippingErrors.NoReturnAddress);
        }

        if (shipment is null)
        {
            var created = await CreateAsync(part, cancellationToken);

            if (created.IsFailure)
            {
                return Result.Failure<ShipmentDto>(created.Error);
            }

            shipment = created.Value;
        }

        Result booked;

        try
        {
            booked = await BookWithCarrierAsync(shipment, part, returnTo, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Booked by someone else at the same moment; their progress was saved.
            return Result.Failure<ShipmentDto>(ShippingErrors.ConcurrentChange);
        }

        return booked.IsFailure ? Result.Failure<ShipmentDto>(booked.Error) : shipment.ToDto(courier);
    }

    /// <summary>
    /// Only the units the buyer is sending back travel: the parcel is sized for them from their
    /// recorded packages, or - when they are the whole parcel, or cannot be sized - taken to be the
    /// box it went out in.
    /// </summary>
    /// <summary>The part as it goes back: only the units the buyer is returning. Requests made before
    /// partial returns named no units, and send the whole part.</summary>
    private static ShippablePartDto Returning(ShippablePartDto part) =>
        part.Lines.Any(l => l.ReturnQuantity > 0)
            ? part with { Lines = [.. part.Lines.Where(l => l.ReturnQuantity > 0).Select(l => l with { Quantity = l.ReturnQuantity, LineTotal = l.UnitPrice * l.ReturnQuantity })] }
            : part;

    private async Task<Result<Shipment>> CreateAsync(ShippablePartDto part, CancellationToken cancellationToken)
    {
        var returning = Returning(part);
        var whole = returning.Lines.Sum(l => l.Quantity) == part.Lines.Sum(l => l.Quantity);
        var planned = whole ? null : (await planner.PlanAsync(returning, cancellationToken)).Parcel;

        var outbound = await dbContext.Set<Shipment>()
            .AsNoTracking()
            .Where(s => s.OrderPartId == part.PartId && s.Direction == ShipmentDirection.Forward && s.Status == ShipmentStatus.Delivered)
            .Select(s => new ParcelDto(s.WeightGrams, s.LengthCm, s.BreadthCm, s.HeightCm))
            .FirstOrDefaultAsync(cancellationToken);

        var parcel = planned ?? outbound ?? (await planner.PlanAsync(returning, cancellationToken)).Parcel;

        if (parcel is null)
        {
            return Result.Failure<Shipment>(ShippingErrors.ParcelDetailsNeeded);
        }

        var shipment = Shipment.CreateReturn(
            part.OrderId,
            part.OrderNumber,
            part.PartId,
            part.BuyerId,
            part.SellerId,
            courier.Name,
            (parcel.WeightGrams, parcel.LengthCm, parcel.BreadthCm, parcel.HeightCm),
            part.ReturnReason);

        dbContext.Set<Shipment>().Add(shipment);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Booked twice at once - the approval's handler and a person, say. The unique index
            // let one through.
            return Result.Failure<Shipment>(ShippingErrors.ConcurrentChange);
        }

        return shipment;
    }

    private async Task<Result> BookWithCarrierAsync(
        Shipment shipment,
        ShippablePartDto part,
        Sellers.Contracts.Dtos.SellerReturnAddressDto returnTo,
        CancellationToken cancellationToken)
    {
        if (shipment.CarrierOrderId is null)
        {
            var order = await courier.CreateReturnOrderAsync(Request(shipment, part, returnTo), cancellationToken);

            if (order.IsFailure)
            {
                return await FailAsync("Creating the return order failed.", order.Error);
            }

            shipment.RecordCarrierOrder(order.Value.CarrierOrderId, order.Value.CarrierShipmentId);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (shipment.Awb is null && shipment.QuotedCourierId is null)
        {
            // Priced from the buyer to the seller, as a return. A return that cannot be priced
            // is still collected; staff enter its charge from the invoice.
            var quote = await courier.QuoteAsync(
                new CourierQuoteRequest(
                    part.DeliveryAddress.Pincode,
                    returnTo.Address.Pincode,
                    shipment.WeightGrams,
                    shipment.LengthCm,
                    shipment.BreadthCm,
                    shipment.HeightCm,
                    CashOnDelivery: false,
                    part.Subtotal,
                    IsReturn: true),
                cancellationToken);

            if (quote.IsSuccess)
            {
                shipment.RecordQuote(quote.Value.CourierId, quote.Value.Freight, quote.Value.CodCharge);
            }
            else
            {
                shipment.RecordQuoteError("The courier could not quote a charge for this return.");
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (shipment.Awb is null)
        {
            var awb = await courier.AssignReturnAwbAsync(shipment.CarrierShipmentId!, shipment.QuotedCourierId, cancellationToken);

            if (awb.IsFailure)
            {
                return await FailAsync("No courier could be assigned to collect the return (AWB).", awb.Error);
            }

            shipment.RecordAwb(awb.Value.Awb, awb.Value.CourierName);
            shipment.RecordPickupRequested(clock.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();

        async Task<Result> FailAsync(string step, Error error)
        {
            shipment.RecordBookingError(step);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Failure(error);
        }
    }

    private CourierReturnRequest Request(
        Shipment shipment,
        ShippablePartDto part,
        Sellers.Contracts.Dtos.SellerReturnAddressDto returnTo)
    {
        var buyer = part.DeliveryAddress;
        var line2 = string.Join(", ", new[] { buyer.Line2, buyer.Landmark, buyer.District }.Where(p => !string.IsNullOrWhiteSpace(p)));

        return new CourierReturnRequest(
            shipment.CarrierReference,
            clock.UtcNow,
            new CourierAddress(buyer.FullName, buyer.Mobile, buyer.Line1, line2.Length > 0 ? line2 : null, buyer.City, buyer.State, buyer.Pincode),
            new CourierAddress(
                returnTo.ShopName,
                returnTo.ContactMobile,
                returnTo.Address.Line1,
                returnTo.Address.Line2,
                returnTo.Address.City,
                returnTo.Address.State,
                returnTo.Address.Pincode),
            [.. Returning(part).Lines.Select(l => new CourierOrderItem(l.Name, l.Sku, l.Quantity, l.UnitPrice))],
            Returning(part).Lines.Sum(l => l.UnitPrice * l.Quantity),
            shipment.WeightGrams,
            shipment.LengthCm,
            shipment.BreadthCm,
            shipment.HeightCm);
    }
}
