using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Shipping.Domain;

/// <summary>Every failure this module can return.</summary>
public static class ShippingErrors
{
    public static readonly Error NotSignedIn = Error.Unauthorized(
        "shipping.not_signed_in",
        "Sign in to track your orders.");

    public static readonly Error NotReadyToShip = Error.Conflict(
        "shipping.part.not_ready",
        "Only a confirmed part that has not shipped can be packed and booked.");

    public static readonly Error ParcelDetailsNeeded = Error.Validation(
        "shipping.parcel.details_needed",
        "Some products have no package recorded. Enter the parcel's weight and size.");

    public static readonly Error NoPickupLocation = Error.Conflict(
        "shipping.pickup.not_set_up",
        "No pickup location is set up for this seller, and there is no platform warehouse to fall back on.");

    public static readonly Error CourierDisabled = Error.Conflict(
        "shipping.courier.disabled",
        "Courier booking is not set up here. Mark the part by hand instead.");

    public static readonly Error CourierUnavailable = Error.Failure(
        "shipping.courier.unavailable",
        "The courier service could not be reached or refused the booking. Try packing again in a moment.");

    public static readonly Error InvalidWebhookToken = Error.Unauthorized(
        "shipping.webhook.invalid_token",
        "The tracking update could not be authenticated.");

    public static readonly Error PickupLocationNotFound = Error.NotFound(
        "shipping.pickup.not_found",
        "No pickup location is set up for that seller.");

    public static readonly Error ConcurrentChange = Error.Conflict(
        "shipping.concurrent_change",
        "The shipment was updated at the same time by something else. Try again.");
}
