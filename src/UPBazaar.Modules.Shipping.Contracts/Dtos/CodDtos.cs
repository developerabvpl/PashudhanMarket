namespace UPBazaar.Modules.Shipping.Contracts.Dtos;

/// <summary>What the courier owes for one delivered cash-on-delivery parcel, and what it has paid.</summary>
/// <param name="Id">Public id.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="OrderPartId">The seller's part the parcel carried.</param>
/// <param name="SellerId">Whose parcel it was.</param>
/// <param name="Awb">The courier's tracking number, as its remittance reports name the parcel.</param>
/// <param name="Expected">What the courier collected at the door.</param>
/// <param name="Received">What it has paid over so far.</param>
/// <param name="Status">Outstanding (nothing yet), ShortPaid (some), Received (all), Over (more than collected) or WrittenOff.</param>
/// <param name="DeliveredAtUtc">When the courier reported it delivered.</param>
/// <param name="IsOverdue">Still owed, and delivered longer ago than the courier's remittance cycle allows.</param>
/// <param name="WriteOffNote">Why staff wrote it off, if they did.</param>
public sealed record CodReceivableDto(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    Guid OrderPartId,
    Guid SellerId,
    string Awb,
    decimal Expected,
    decimal Received,
    string Status,
    DateTime DeliveredAtUtc,
    bool IsOverdue,
    string? WriteOffNote);

/// <summary>Cash on delivery at a glance.</summary>
/// <param name="OutstandingAmount">What the courier still owes, across every parcel it owes for.</param>
/// <param name="OutstandingCount">How many parcels it owes for, in full or in part.</param>
/// <param name="OverdueCount">Of those, how many are overdue.</param>
/// <param name="ShortCount">How many it has paid only part of.</param>
/// <param name="OverdueDays">Days after delivery a parcel's cash is overdue.</param>
public sealed record CodSummaryDto(
    decimal OutstandingAmount,
    int OutstandingCount,
    int OverdueCount,
    int ShortCount,
    int OverdueDays);

/// <summary>One bank transfer from the courier, as uploaded from its remittance report.</summary>
/// <param name="Id">Public id.</param>
/// <param name="Reference">The bank reference (UTR) of the transfer. Unique: a report is uploaded once.</param>
/// <param name="RemittedOn">The day the money arrived.</param>
/// <param name="Total">What the report adds up to.</param>
/// <param name="UnmatchedCount">Rows whose AWB matched no delivered cash-on-delivery parcel yet.</param>
/// <param name="FileName">The file uploaded.</param>
/// <param name="UploadedAtUtc">When.</param>
/// <param name="UploadedBy">By whom.</param>
/// <param name="Lines">Every row, matched or not.</param>
public sealed record CodRemittanceDto(
    Guid Id,
    string Reference,
    DateOnly RemittedOn,
    decimal Total,
    int UnmatchedCount,
    string FileName,
    DateTime UploadedAtUtc,
    string? UploadedBy,
    IReadOnlyList<CodRemittanceLineDto> Lines);

/// <summary>One row of a remittance report.</summary>
/// <param name="Awb">The parcel, as the report names it.</param>
/// <param name="Amount">What was paid for it.</param>
/// <param name="Matched">Whether it was matched to a delivered cash-on-delivery parcel.</param>
/// <param name="OrderNumber">The matched parcel's order, if matched.</param>
public sealed record CodRemittanceLineDto(string Awb, decimal Amount, bool Matched, string? OrderNumber);
