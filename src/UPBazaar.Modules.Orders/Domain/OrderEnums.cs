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
}
