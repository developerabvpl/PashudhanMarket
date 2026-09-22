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
/// it moves the parcel - collected, delivered - the order's part is moved to match.
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

        var shipment = await dbContext.Set<Shipment>().Include(s => s.Events).FirstOrDefaultAsync(s => s.Awb == awb, cancellationToken);

        if (shipment is null)
        {
            LogUnknownAwb(logger, awb);

            return Result.Success();
        }

        var moved = shipment.ApplyCourierStatus(rawStatus, CourierStatus.Map(rawStatus), clock.UtcNow);

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

        var partStatus = shipment.Status switch
        {
            ShipmentStatus.InTransit => "Shipped",
            ShipmentStatus.Delivered => "Delivered",
            _ => null,
        };

        if (!moved || partStatus is null)
        {
            return Result.Success();
        }

        return await orders.AdvancePartAsync(shipment.OrderId, shipment.OrderPartId, partStatus, cancellationToken);
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
