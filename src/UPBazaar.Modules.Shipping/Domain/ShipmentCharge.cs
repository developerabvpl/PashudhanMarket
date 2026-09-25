using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Shipping.Domain;

/// <summary>A courier journey that is charged for. Stored by name.</summary>
public enum CourierTrip
{
    /// <summary>The parcel's journey from the seller to the buyer.</summary>
    Delivery = 0,

    /// <summary>The journey back after the courier could not deliver (return to origin).</summary>
    Rto = 1,

    /// <summary>Collecting a buyer's return and taking it to the seller.</summary>
    ReturnPickup = 2,
}

/// <summary>
/// What the courier charges for one trip of a shipment.
///
/// The amount starts as Shiprocket's quote and can be corrected by staff when the invoice differs -
/// after the courier re-weighs a parcel, say. Nothing is owed until the trip happens: a parcel
/// booked and then cancelled before collection costs nothing. Once it has happened, every change
/// is passed on as the difference from what was already charged, so the seller's statement shows
/// the original charge and each correction.
/// </summary>
public sealed class ShipmentCharge : Entity
{
    private ShipmentCharge()
    {
    }

    public long ShipmentId { get; private set; }

    public CourierTrip Trip { get; private set; }

    /// <summary>What the trip costs, as quoted or corrected. Null while unknown.</summary>
    public decimal? Amount { get; private set; }

    /// <summary>How much has been passed on to be charged so far.</summary>
    public decimal Billed { get; private set; }

    /// <summary>How many times it has been passed on: the original charge, then each correction.</summary>
    public int BillCount { get; private set; }

    /// <summary>When the trip happened; null until then.</summary>
    public DateTime? IncurredAtUtc { get; private set; }

    /// <summary>User id of whoever last corrected the amount.</summary>
    public string? CorrectedBy { get; private set; }

    public DateTime? CorrectedAtUtc { get; private set; }

    /// <summary>Why it was corrected: the invoice line, say.</summary>
    public string? Note { get; private set; }

    public bool IsIncurred => IncurredAtUtc is not null;

    internal static ShipmentCharge For(CourierTrip trip, decimal? amount) => new() { Trip = trip, Amount = amount };

    internal void Quote(decimal amount) => Amount = amount;

    internal void Correct(decimal amount, string? by, string? note, DateTime now)
    {
        Amount = amount;
        CorrectedBy = by;
        CorrectedAtUtc = now;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    internal void Incur(DateTime now) => IncurredAtUtc ??= now;

    /// <summary>
    /// What is still to be passed on: the amount less what already was. Zero when there is nothing
    /// new - the trip has not happened, the amount is unknown, or nothing has changed.
    /// </summary>
    internal decimal TakeUnbilled()
    {
        if (!IsIncurred || Amount is not { } amount || amount == Billed)
        {
            return 0m;
        }

        var due = amount - Billed;
        Billed = amount;
        BillCount++;

        return due;
    }
}
