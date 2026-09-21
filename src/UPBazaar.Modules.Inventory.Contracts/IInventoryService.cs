using UPBazaar.Modules.Inventory.Contracts.Dtos;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Inventory.Contracts;

/// <summary>
/// The inventory side of every cross-module conversation: Catalog asks how much there is, and
/// Cart and Orders will hold, release and consume stock through here.
///
/// A reservation is how a checkout makes sure the goods still exist when payment lands. It holds
/// stock without removing it, expires on its own if nobody completes it, and is either committed
/// (the goods leave) or released (they go back on sale).
/// </summary>
public interface IInventoryService
{
    /// <summary>
    /// Stock for each product asked about. A product Inventory has never counted comes back with
    /// zeros rather than being left out, so a caller can index the result without checking.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, StockLevelDto>> GetStockLevelsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a new product's opening stock. Staged, not saved: the caller saves it in the same
    /// transaction as the product itself, so a product never exists without its stock row.
    /// </summary>
    Task<Result> StageOpeningStockAsync(Guid productId, int quantity, CancellationToken cancellationToken);

    /// <summary>
    /// Holds every line or none of them. Fails with a conflict if any product is short, so a
    /// checkout never ends up holding half an order.
    /// </summary>
    /// <param name="reference">What the hold is for, such as an order number. Shown in the ledger.</param>
    /// <param name="lines">Products and quantities. A product may appear only once.</param>
    /// <param name="holdFor">How long before an unfinished hold is released automatically.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The reservation id, needed to release or commit it.</returns>
    Task<Result<Guid>> ReserveAsync(
        string reference,
        IReadOnlyList<ReservationLineDto> lines,
        TimeSpan holdFor,
        CancellationToken cancellationToken);

    /// <summary>Puts held stock back on sale. Releasing an already-finished reservation is a no-op.</summary>
    Task<Result> ReleaseAsync(Guid reservationId, CancellationToken cancellationToken);

    /// <summary>
    /// The goods have left: removes the held quantities from stock on hand. Fails if the
    /// reservation already expired or was released, because that stock may have been sold again.
    /// </summary>
    Task<Result> CommitAsync(Guid reservationId, CancellationToken cancellationToken);

    /// <summary>
    /// Puts committed stock back on sale, for an order cancelled after its stock was committed
    /// but before anything left the seller. Every line or none.
    /// </summary>
    /// <param name="reference">What the stock is coming back from, such as an order number.</param>
    /// <param name="lines">Products and quantities. A product may appear only once.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> ReturnAsync(
        string reference,
        IReadOnlyList<ReservationLineDto> lines,
        CancellationToken cancellationToken);
}
