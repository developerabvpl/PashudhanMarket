namespace UPBazaar.Modules.Orders.Domain;

/// <summary>
/// A buyer asking to send a delivered part back, and what the seller or staff decided.
///
/// One per part: a refusal is final here, so a buyer cannot wear a seller down by asking again,
/// and a disputed refusal goes to support instead. Owned by <see cref="OrderPart"/> and stored
/// in its row, since it never exists apart from the part.
/// </summary>
public sealed class ReturnRequest
{
    private ReturnRequest()
    {
    }

    public ReturnReason Reason { get; private set; }

    /// <summary>The buyer's own words. Required when the reason is Other.</summary>
    public string? Comment { get; private set; }

    /// <summary>
    /// Where a cash-on-delivery refund is paid, since there is no online payment to reverse. Null
    /// for an order paid online, whose refund goes back through Razorpay.
    /// </summary>
    public string? RefundUpiId { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }

    public ReturnRequestStatus Status { get; private set; }

    /// <summary>Why it was refused, shown to the buyer; or a note left when approving.</summary>
    public string? DecisionNote { get; private set; }

    /// <summary>User id of the seller or staff member who decided.</summary>
    public string? DecidedBy { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>
    /// What is refunded when the goods are back, worked out on approval: what the buyer paid for
    /// the returned units, less any coupon discount taken back. Null until approved, and for
    /// requests approved before partial returns, which refund the whole part.
    /// </summary>
    public decimal? RefundDue { get; private set; }

    internal static ReturnRequest Create(ReturnReason reason, string? comment, string? refundUpiId, DateTime now) => new()
    {
        Reason = reason,
        Comment = Normalize(comment),
        RefundUpiId = Normalize(refundUpiId),
        RequestedAtUtc = now,
        Status = ReturnRequestStatus.Requested,
    };

    internal void Decide(bool approve, string? note, string? decidedBy, DateTime now)
    {
        Status = approve ? ReturnRequestStatus.Approved : ReturnRequestStatus.Rejected;
        DecisionNote = Normalize(note);
        DecidedBy = decidedBy;
        DecidedAtUtc = now;
    }

    internal void SetRefund(decimal amount) => RefundDue = amount;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
