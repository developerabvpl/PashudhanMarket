namespace UPBazaar.Modules.Shipping.Contracts;

/// <summary>
/// Whether the courier has paid over the cash it collected for a parcel. Settlements asks when it
/// records a cash-on-delivery earning, in case the remittance got there first.
/// </summary>
public interface ICodCash
{
    /// <summary>
    /// True once the cash for the part's parcel is in - paid over in full, or written off. False
    /// while it is owed, and for a part whose delivery the courier has not reported yet.
    /// </summary>
    Task<bool> IsCashInAsync(Guid orderPartId, CancellationToken cancellationToken);
}
