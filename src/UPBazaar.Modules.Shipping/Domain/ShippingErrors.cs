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

    public static readonly Error PartNotFound = Error.NotFound(
        "shipping.part.not_found",
        "That part of the order was not found.");

    public static readonly Error NoApprovedReturn = Error.Conflict(
        "shipping.return.not_approved",
        "This parcel has no approved return waiting for a pickup.");

    public static readonly Error NoReturnAddress = Error.Conflict(
        "shipping.return.no_address",
        "The seller has no registered address to send the return to.");

    public static readonly Error ShipmentNotFound = Error.NotFound(
        "shipping.shipment.not_found",
        "Shipment not found.");

    public static readonly Error TripNotOnShipment = Error.Validation(
        "shipping.charge.wrong_trip",
        "That trip is not one this shipment makes: a delivery can be charged for its delivery and its RTO, a return for its pickup.");

    public static readonly Error CodNothingOwed = Error.Conflict(
        "shipping.cod.nothing_owed",
        "The courier owes nothing more for this parcel.");

    public static readonly Error CodReceivableNotFound = Error.NotFound(
        "shipping.cod.not_found",
        "No cash-on-delivery parcel with that id.");

    public static readonly Error CodRemittanceNotFound = Error.NotFound(
        "shipping.cod.remittance_not_found",
        "No remittance with that id.");

    public static readonly Error CodReferenceTaken = Error.Conflict(
        "shipping.cod.reference_taken",
        "A remittance with this bank reference has already been uploaded.");

    public static Error CodFileUnreadable(string why) => Error.Validation(
        "shipping.cod.file_unreadable",
        why);

    public static readonly Error ConcurrentChange = Error.Conflict(
        "shipping.concurrent_change",
        "The shipment was updated at the same time by something else. Try again.");
}
