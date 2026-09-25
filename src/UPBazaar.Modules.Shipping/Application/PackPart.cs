using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Application;

/// <summary>The parcel the packing form should start from, and where it will be collected.</summary>
public sealed record GetParcelSuggestionQuery(Guid OrderId, Guid PartId) : IQuery<ParcelSuggestionDto>;

internal sealed class GetParcelSuggestionQueryHandler(IOrderFulfilmentService orders, ParcelPlanner planner)
    : IQueryHandler<GetParcelSuggestionQuery, ParcelSuggestionDto>
{
    public async Task<Result<ParcelSuggestionDto>> HandleAsync(
        GetParcelSuggestionQuery query,
        CancellationToken cancellationToken)
    {
        var part = await orders.GetPartAsync(query.OrderId, query.PartId, cancellationToken);

        if (part.IsFailure)
        {
            return Result.Failure<ParcelSuggestionDto>(part.Error);
        }

        var (parcel, missing) = await planner.PlanAsync(part.Value, cancellationToken);

        return new ParcelSuggestionDto(parcel, missing, await planner.PickupLocationForAsync(part.Value.SellerId, cancellationToken));
    }
}

/// <summary>
/// A seller's part is packed: book the courier and mark the part Packed.
/// </summary>
/// <param name="OrderId">The order.</param>
/// <param name="PartId">The seller's part.</param>
/// <param name="Parcel">The parcel as actually packed. Optional when every product has a package recorded.</param>
public sealed record PackPartCommand(Guid OrderId, Guid PartId, ParcelDto? Parcel) : ICommand<ShipmentDto>;

internal sealed class PackPartCommandValidator : AbstractValidator<PackPartCommand>
{
    public PackPartCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.PartId).NotEmpty();

        When(x => x.Parcel is not null, () =>
        {
            RuleFor(x => x.Parcel!.WeightGrams).InclusiveBetween(1, 50_000).OverridePropertyName("WeightGrams");
            RuleFor(x => x.Parcel!.LengthCm).InclusiveBetween(0.1m, 200m).OverridePropertyName("LengthCm");
            RuleFor(x => x.Parcel!.BreadthCm).InclusiveBetween(0.1m, 200m).OverridePropertyName("BreadthCm");
            RuleFor(x => x.Parcel!.HeightCm).InclusiveBetween(0.1m, 200m).OverridePropertyName("HeightCm");
        });
    }
}

/// <summary>
/// Books the courier in the carrier's three steps - order, AWB, pickup - saving after each, then
/// tells Orders the part is packed.
///
/// A step that fails leaves the shipment in Booking with the error on it, and the part as it was.
/// Packing the same part again picks up from the step that failed: the shipment already holds the
/// carrier's ids from the steps that worked, so nothing is booked twice. Once a part has a finished
/// booking, packing it again simply returns that shipment.
/// </summary>
internal sealed class PackPartCommandHandler(
    UPBazaarDbContext dbContext,
    IOrderFulfilmentService orders,
    ParcelPlanner planner,
    ICourierGateway courier,
    IClock clock) : ICommandHandler<PackPartCommand, ShipmentDto>
{
    public async Task<Result<ShipmentDto>> HandleAsync(PackPartCommand command, CancellationToken cancellationToken)
    {
        if (!courier.IsEnabled)
        {
            return Result.Failure<ShipmentDto>(ShippingErrors.CourierDisabled);
        }

        var found = await orders.GetPartAsync(command.OrderId, command.PartId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<ShipmentDto>(found.Error);
        }

        var part = found.Value;

        var shipment = await dbContext.Set<Shipment>()
            .Include(s => s.Events)
            .Include(s => s.Charges)
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                s => s.OrderPartId == part.PartId && s.Direction == ShipmentDirection.Forward && s.Status != ShipmentStatus.Cancelled,
                cancellationToken);

        if (part.Status is "Cancelled" or "AwaitingPayment")
        {
            return Result.Failure<ShipmentDto>(ShippingErrors.NotReadyToShip);
        }

        if (shipment is not null && shipment.Status != ShipmentStatus.Booking)
        {
            // Booked already. Telling Orders again is harmless and repairs the case where the
            // booking finished but marking the part packed did not.
            var repaired = await orders.AdvancePartAsync(part.OrderId, part.PartId, "Packed", cancellationToken);

            return repaired.IsFailure ? Result.Failure<ShipmentDto>(repaired.Error) : shipment.ToDto(courier);
        }

        if (shipment is null)
        {
            if (part.Status != "Confirmed")
            {
                return Result.Failure<ShipmentDto>(ShippingErrors.NotReadyToShip);
            }

            var created = await CreateAsync(part, command.Parcel, cancellationToken);

            if (created.IsFailure)
            {
                return Result.Failure<ShipmentDto>(created.Error);
            }

            shipment = created.Value;
        }

        var booked = await BookAsync(shipment, part, cancellationToken);

        if (booked.IsFailure)
        {
            return Result.Failure<ShipmentDto>(booked.Error);
        }

        var packed = await orders.AdvancePartAsync(part.OrderId, part.PartId, "Packed", cancellationToken);

        return packed.IsFailure ? Result.Failure<ShipmentDto>(packed.Error) : shipment.ToDto(courier);
    }

    private async Task<Result<Shipment>> CreateAsync(ShippablePartDto part, ParcelDto? given, CancellationToken cancellationToken)
    {
        var parcel = given;

        if (parcel is null)
        {
            (parcel, _) = await planner.PlanAsync(part, cancellationToken);

            if (parcel is null)
            {
                return Result.Failure<Shipment>(ShippingErrors.ParcelDetailsNeeded);
            }
        }

        var pickup = await planner.PickupLocationForAsync(part.SellerId, cancellationToken);

        if (pickup is null)
        {
            return Result.Failure<Shipment>(ShippingErrors.NoPickupLocation);
        }

        var shipment = Shipment.Create(
            part.OrderId,
            part.OrderNumber,
            part.PartId,
            part.BuyerId,
            part.SellerId,
            courier.Name,
            pickup,
            (parcel.WeightGrams, parcel.LengthCm, parcel.BreadthCm, parcel.HeightCm),
            part.CodAmount);

        dbContext.Set<Shipment>().Add(shipment);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two people packed the same part at once; the unique index let one booking through.
            return Result.Failure<Shipment>(ShippingErrors.ConcurrentChange);
        }

        return shipment;
    }

    private async Task<Result> BookAsync(Shipment shipment, ShippablePartDto part, CancellationToken cancellationToken)
    {
        if (shipment.CarrierOrderId is null)
        {
            var order = await courier.CreateOrderAsync(Request(shipment, part), cancellationToken);

            if (order.IsFailure)
            {
                return await FailAsync(shipment, "Creating the courier order failed.", order.Error);
            }

            shipment.RecordCarrierOrder(order.Value.CarrierOrderId, order.Value.CarrierShipmentId);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (shipment.Awb is null && shipment.QuotedCourierId is null)
        {
            await QuoteAsync(shipment, part, cancellationToken);
        }

        if (shipment.Awb is null)
        {
            var awb = await courier.AssignAwbAsync(shipment.CarrierShipmentId!, shipment.QuotedCourierId, cancellationToken);

            if (awb.IsFailure)
            {
                return await FailAsync(shipment, "No courier could be assigned (AWB).", awb.Error);
            }

            shipment.RecordAwb(awb.Value.Awb, awb.Value.CourierName);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (shipment.PickupRequestedAtUtc is null)
        {
            var pickup = await courier.RequestPickupAsync(shipment.CarrierShipmentId!, cancellationToken);

            if (pickup.IsFailure)
            {
                return await FailAsync(shipment, "Requesting the pickup failed.", pickup.Error);
            }

            shipment.RecordPickupRequested(clock.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();

        async Task<Result> FailAsync(Shipment failed, string step, SharedKernel.Results.Error error)
        {
            failed.RecordBookingError(step);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Result.Failure(error);
        }
    }

    /// <summary>
    /// Prices the parcel before the AWB is asked for, so the courier assigned is the one quoted
    /// for. A parcel that cannot be priced still ships: its charge is left for staff to enter from
    /// the invoice, rather than holding up the buyer's delivery.
    /// </summary>
    private async Task QuoteAsync(Shipment shipment, ShippablePartDto part, CancellationToken cancellationToken)
    {
        var pickupPincode = await planner.PickupPincodeForAsync(part.SellerId, cancellationToken);

        if (pickupPincode is null)
        {
            shipment.RecordQuoteError("The pickup location has no PIN code, so the courier charge could not be quoted.");
        }
        else
        {
            var quote = await courier.QuoteAsync(
                new CourierQuoteRequest(
                    pickupPincode,
                    part.DeliveryAddress.Pincode,
                    shipment.WeightGrams,
                    shipment.LengthCm,
                    shipment.BreadthCm,
                    shipment.HeightCm,
                    shipment.CodAmount > 0,
                    part.Subtotal,
                    IsReturn: false),
                cancellationToken);

            if (quote.IsSuccess)
            {
                shipment.RecordQuote(quote.Value.CourierId, quote.Value.Freight, quote.Value.CodCharge);
            }
            else
            {
                shipment.RecordQuoteError("The courier could not quote a charge for this parcel.");
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static CourierOrderRequest Request(Shipment shipment, ShippablePartDto part)
    {
        var address = part.DeliveryAddress;
        var line2 = string.Join(", ", new[] { address.Line2, address.Landmark, address.District }.Where(p => !string.IsNullOrWhiteSpace(p)));

        return new CourierOrderRequest(
            shipment.CarrierReference,
            part.PlacedAtUtc,
            shipment.PickupLocation,
            address.FullName,
            address.Mobile,
            address.Line1,
            line2.Length > 0 ? line2 : null,
            address.City,
            address.State,
            address.Pincode,
            [.. part.Lines.Select(l => new CourierOrderItem(l.Name, l.Sku, l.Quantity, l.UnitPrice))],
            shipment.CodAmount > 0,
            part.Subtotal,
            part.DeliveryFee,
            shipment.WeightGrams,
            shipment.LengthCm,
            shipment.BreadthCm,
            shipment.HeightCm);
    }
}
