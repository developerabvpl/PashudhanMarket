using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Domain;

public static class ShippingErrors
{
    public static readonly Error NotFound =
        Error.NotFound("shipping.shipment.not_found", "The shipment does not exist.");

    public static readonly Error AlreadyBooked =
        Error.Conflict("shipping.shipment.already_booked", "The shipment is already booked.");

    public static readonly Error NotCancellable =
        Error.Conflict("shipping.shipment.not_cancellable", "A delivered shipment cannot be cancelled.");
}
