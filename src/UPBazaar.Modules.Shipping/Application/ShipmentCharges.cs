using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Shipping.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Application;

/// <summary>
/// Staff set what a courier trip really cost, from Shiprocket's invoice: when a parcel could not be
/// priced at booking, or the courier re-weighed it and billed more. The seller is charged the
/// difference from what was charged before.
/// </summary>
/// <param name="ShipmentId">The shipment.</param>
/// <param name="Trip">Delivery, Rto or ReturnPickup.</param>
/// <param name="Amount">What the trip cost, in rupees.</param>
/// <param name="Note">Where the figure comes from: the invoice, say.</param>
public sealed record CorrectShipmentChargeCommand(Guid ShipmentId, string Trip, decimal Amount, string? Note) : ICommand<ShipmentDto>;

internal sealed class CorrectShipmentChargeCommandValidator : AbstractValidator<CorrectShipmentChargeCommand>
{
    public CorrectShipmentChargeCommandValidator()
    {
        RuleFor(x => x.ShipmentId).NotEmpty();
        RuleFor(x => x.Trip)
            .Must(t => Enum.TryParse<CourierTrip>(t, ignoreCase: true, out _))
            .WithMessage("Trip must be Delivery, Rto or ReturnPickup.");
        RuleFor(x => x.Amount).InclusiveBetween(0m, 100_000m).PrecisionScale(8, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.Note).MaximumLength(200);
    }
}

internal sealed class CorrectShipmentChargeCommandHandler(
    UPBazaarDbContext dbContext,
    ICourierGateway courier,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<CorrectShipmentChargeCommand, ShipmentDto>
{
    public async Task<Result<ShipmentDto>> HandleAsync(CorrectShipmentChargeCommand command, CancellationToken cancellationToken)
    {
        var shipment = await dbContext.Set<Shipment>()
            .Include(s => s.Events)
            .Include(s => s.Charges)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.PublicId == command.ShipmentId, cancellationToken);

        if (shipment is null)
        {
            return Result.Failure<ShipmentDto>(ShippingErrors.ShipmentNotFound);
        }

        var trip = Enum.Parse<CourierTrip>(command.Trip, ignoreCase: true);
        var corrected = shipment.CorrectCharge(trip, command.Amount, currentUser.UserId, command.Note, clock.UtcNow);

        if (corrected.IsFailure)
        {
            return Result.Failure<ShipmentDto>(corrected.Error);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A courier update landed at the same moment; the correction may no longer be right.
            return Result.Failure<ShipmentDto>(ShippingErrors.ConcurrentChange);
        }

        return shipment.ToDto(courier);
    }
}
