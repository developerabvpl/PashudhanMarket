namespace UPBazaar.Modules.Orders.Domain;

/// <summary>Where an order as a whole stands. Stored by name.</summary>
public enum OrderStatus
{
    /// <summary>Placed for online payment; stock is held until the money arrives or the deadline passes.</summary>
    PendingPayment = 0,

    /// <summary>Paid online, or placed as cash on delivery. Stock is committed and sellers can fulfil.</summary>
    Confirmed = 1,

    /// <summary>Every part that was not cancelled has been delivered.</summary>
    Completed = 2,

    /// <summary>Nothing in it is coming.</summary>
    Cancelled = 3,
}

/// <summary>How the buyer chose to pay.</summary>
public enum PaymentMethod
{
    CashOnDelivery = 0,

    /// <summary>Paid through the payment provider before the order is fulfilled.</summary>
    Online = 1,
}

/// <summary>Where the money stands, as far as Orders knows it.</summary>
public enum PaymentStatus
{
    /// <summary>An online order not yet paid.</summary>
    Pending = 0,

    /// <summary>Paid online.</summary>
    Paid = 1,

    /// <summary>To be collected at the door; the courier's remittance is Payments' business, not ours.</summary>
    CashOnDelivery = 2,
}

/// <summary>Where one seller's part of an order stands. Moves forward only, or to Cancelled.</summary>
public enum OrderPartStatus
{
    /// <summary>The order is waiting for online payment; the seller must not pack yet.</summary>
    AwaitingPayment = 0,

    Confirmed = 1,

    Packed = 2,

    /// <summary>Handed to the courier. From here the part can no longer be cancelled, only returned.</summary>
    Shipped = 3,

    Delivered = 4,

    Cancelled = 5,

    /// <summary>
    /// Going back to the seller: the courier could not deliver it (RTO), or the buyer's return was
    /// approved. Numbered after the others on purpose: a part only moves to a higher number, so a
    /// late "delivered" update can never undo a return already under way.
    /// </summary>
    Returning = 6,

    /// <summary>Back with the seller, waiting for them to inspect it, or inspected.</summary>
    Returned = 7,
}

/// <summary>What the seller found when a returned parcel came back.</summary>
public enum ReturnCondition
{
    /// <summary>Resaleable: its stock goes back on sale.</summary>
    Good = 0,

    /// <summary>Not resaleable: nothing goes back on sale.</summary>
    Damaged = 1,
}

/// <summary>Why a buyer wants to send a delivered parcel back.</summary>
public enum ReturnReason
{
    /// <summary>Arrived broken, leaking or crushed.</summary>
    Damaged = 0,

    /// <summary>Not what was ordered.</summary>
    WrongItem = 1,

    /// <summary>Differs from the listing's description.</summary>
    NotAsDescribed = 2,

    /// <summary>Spoilt, stale or poorly made.</summary>
    QualityIssue = 3,

    /// <summary>Nothing wrong with it; the buyer changed their mind. The seller may refuse.</summary>
    NoLongerNeeded = 4,

    /// <summary>Anything else; the buyer must say what.</summary>
    Other = 5,
}

/// <summary>Where a buyer's return request stands. What happens to the goods is the part's status.</summary>
public enum ReturnRequestStatus
{
    /// <summary>Waiting for the seller, or staff, to decide.</summary>
    Requested = 0,

    /// <summary>Accepted: a return pickup is booked and the part is on its way back.</summary>
    Approved = 1,

    /// <summary>Refused, with a note saying why. Final: the buyer can take it up with support.</summary>
    Rejected = 2,
}
