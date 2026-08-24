using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Contracts;

/// <summary>
/// Stock movements requested by other modules. Reservation is all-or-nothing: either every
/// line is held or none is, so a partially stocked cart never becomes a partial order.
/// </summary>
public interface IStockReservations
{
    Task<Result<IReadOnlyList<ReservedItemDto>>> ReserveAsync(
        IReadOnlyCollection<StockReservationLine> lines,
        CancellationToken cancellationToken);

    Task<Result> ReleaseAsync(
        IReadOnlyCollection<StockReservationLine> lines,
        CancellationToken cancellationToken);
}
