using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Orders.Contracts;
using UPBazaar.Modules.Shipping.Domain;
using UPBazaar.Modules.Shipping.Gateway;
using UPBazaar.SharedKernel.Abstractions;
using UPBazaar.SharedKernel.Messaging;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Application;

/// <summary>A tracking update as received, with the token Shiprocket sends in <c>x-api-key</c>.</summary>
public sealed record HandleCourierWebhookCommand(string RawBody, string? Token) : ICommand;

/// <summary>
/// The courier reporting progress. The update is found by AWB, recorded on the shipment, and when
/// it moves the parcel - collected, delivered, or sent back undelivered and back with the seller -
/// the order's part is moved to match.
///
/// Updates arrive late, twice and out of order, so both halves are forgiving: a shipment only
/// moves forward, and Orders treats a part already at or past the status as done. An update for
/// an AWB this platform did not book is acknowledged and logged, so it is not retried forever.
/// </summary>
internal sealed partial class HandleCourierWebhookCommandHandler(
    UPBazaarDbContext dbContext,
    ICourierGateway courier,
    IOrderFulfilmentService orders,
    IClock clock,
    ILogger<HandleCourierWebhookCommandHandler> logger) : ICommandHandler<HandleCourierWebhookCommand>
{
    public async Task<Result> HandleAsync(HandleCourierWebhookCommand command, CancellationToken cancellationToken)
    {
        if (!courier.IsWebhookTokenValid(command.Token))
        {
            return Result.Failure(ShippingErrors.InvalidWebhookToken);
        }

        string? awb;
        string? rawStatus;

        try
        {
            using var document = JsonDocument.Parse(command.RawBody);
            awb = Text(document.RootElement, "awb");
            rawStatus = Text(document.RootElement, "current_status");
        }
        catch (JsonException)
        {
            LogUnreadable(logger);

            return Result.Success();
        }

        if (awb is null || rawStatus is null)
        {
            return Result.Success();
        }

        var shipment = await dbContext.Set<Shipment>()
            .Include(s => s.Events)
            .Include(s => s.Charges)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Awb == awb, cancellationToken);

        if (shipment is null)
        {
            LogUnknownAwb(logger, awb);

            return Result.Success();
        }

        var isReturn = shipment.Direction == ShipmentDirection.Return;
        var mapped = isReturn ? CourierStatus.MapReturn(rawStatus) : CourierStatus.Map(rawStatus);
        var now = clock.UtcNow;
        var moved = shipment.ApplyCourierStatus(rawStatus, mapped, now);

        var opensCod = moved && shipment is { Direction: ShipmentDirection.Forward, Status: ShipmentStatus.Delivered, CodAmount: > 0 };

        if (opensCod)
        {
            await OpenCodReceivableAsync(shipment, now, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Something else changed the shipment at the same moment; the courier will send the
            // next update, and this one's event is lost at worst.
            return Result.Failure(ShippingErrors.ConcurrentChange);
        }

        if (opensCod)
        {
            // A remittance uploaded while this was being saved may have missed the parcel.
            await CodMatching.SweepAsync(dbContext, [shipment.Awb!], now, cancellationToken);
        }

        // A buyer's return moves its part only on arrival: the part has been Returning since the
        // return was approved, and a return reaching the seller is the part coming back.
        var partStatus = (isReturn, shipment.Status) switch
        {
            (true, ShipmentStatus.Delivered) => "Returned",
            (true, _) => null,
            (false, ShipmentStatus.InTransit) => "Shipped",
            (false, ShipmentStatus.Delivered) => "Delivered",
            (false, ShipmentStatus.ReturnInTransit) => "Returning",
            (false, ShipmentStatus.Returned) => "Returned",
            _ => null,
        };

        // Told to Orders whenever the shipment's status means something to the part, not only when
        // this update moved it: if telling Orders failed last time - two parcels of one order
        // delivered at once, say - the courier's retry must get through. Orders ignores repeats.
        if (partStatus is null)
        {
            return Result.Success();
        }

        return await orders.AdvancePartAsync(shipment.OrderId, shipment.OrderPartId, partStatus, cancellationToken);
    }

    /// <summary>
    /// The courier now owes the cash it collected at the door. A remittance report that paid for
    /// the parcel before this update arrived is matched to it here.
    /// </summary>
    private async Task OpenCodReceivableAsync(Shipment shipment, DateTime now, CancellationToken cancellationToken)
    {
        var receivable = CodReceivable.Open(shipment, now);
        dbContext.Set<CodReceivable>().Add(receivable);

        var paidEarlier = await dbContext.Set<CodRemittanceLine>()
            .Where(l => l.Awb == receivable.Awb && l.ReceivableId == null)
            .ToListAsync(cancellationToken);

        foreach (var line in paidEarlier)
        {
            line.MatchTo(receivable, now);
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored a courier update whose body could not be read")]
    private static partial void LogUnreadable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Courier update for unknown AWB {Awb}")]
    private static partial void LogUnknownAwb(ILogger logger, string awb);
}
