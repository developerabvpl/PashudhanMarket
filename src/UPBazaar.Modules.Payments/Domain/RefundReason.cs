namespace UPBazaar.Modules.Payments.Domain;

/// <summary>
/// Why money is owed back, as a code the portals translate. The English sentence is still written
/// beside it as the audit record, but a sentence cannot be shown in Hindi, and matching on it breaks
/// the day someone rewords it. Stored by name, so the values must not be renamed.
/// </summary>
public enum RefundReason
{
    /// <summary>The whole order was cancelled after it was paid for.</summary>
    OrderCancelled = 0,

    /// <summary>One seller's part was cancelled; the rest of the order goes ahead.</summary>
    PartCancelled = 1,

    /// <summary>The courier could not deliver the parcel and took it back to the seller (RTO).</summary>
    Undelivered = 2,

    /// <summary>The buyer sent the parcel back and it has reached the seller.</summary>
    BuyerReturn = 3,

    /// <summary>
    /// The money arrived but the order would not take it - cancelled already, past its payment
    /// deadline, a different amount - so all of it goes back.
    /// </summary>
    PaymentRefused = 4,
}

/// <summary>
/// The sentence recorded with each reason. Kept with the codes so the two cannot drift apart; the
/// migration that gave old refunds their codes matched on exactly these sentences.
/// </summary>
public static class RefundReasons
{
    public const string OrderCancelled = "The order was cancelled.";

    public const string PartCancelled = "Part of the order was cancelled.";

    public const string Undelivered = "The parcel could not be delivered and went back to the seller.";

    public const string BuyerReturn = "The buyer returned the parcel and it is back with the seller.";

    /// <summary>Followed by what Orders said, which is what tells staff which refusal it was.</summary>
    public const string PaymentRefusedPrefix = "Payment could not be applied to the order: ";

    /// <summary>The sentence for a reason that needs no detail.</summary>
    public static string Describe(RefundReason reason) => reason switch
    {
        RefundReason.OrderCancelled => OrderCancelled,
        RefundReason.PartCancelled => PartCancelled,
        RefundReason.Undelivered => Undelivered,
        RefundReason.BuyerReturn => BuyerReturn,
        RefundReason.PaymentRefused => PaymentRefusedPrefix.TrimEnd(' ', ':') + ".",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
