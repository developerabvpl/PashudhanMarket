using UPBazaar.SharedKernel.Primitives;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Modules.Catalog.Domain;

/// <summary>
/// Availability for one product. Carries a rowversion because two checkouts can race for the
/// last unit; the concurrency token is what makes the loser retry instead of oversell.
/// </summary>
public sealed class Stock : Entity, IAuditable
{
    private Stock()
    {
    }

    public long ProductId { get; private set; }

    public int OnHand { get; private set; }

    public int Reserved { get; private set; }

    public int Available => OnHand - Reserved;

    /// <summary>SQL Server rowversion; EF uses it as the optimistic concurrency token.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public static Stock Create(int onHand = 0) => new() { OnHand = onHand };

    /// <summary>Holds units for an order that has not been paid for yet.</summary>
    public Result Reserve(int quantity)
    {
        if (quantity <= 0)
        {
            return Result.Failure(CatalogErrors.InvalidQuantity);
        }

        if (quantity > Available)
        {
            return Result.Failure(CatalogErrors.InsufficientStock);
        }

        Reserved += quantity;
        return Result.Success();
    }

    /// <summary>Returns held units to the available pool, e.g. when an order is abandoned.</summary>
    public Result Release(int quantity)
    {
        if (quantity <= 0)
        {
            return Result.Failure(CatalogErrors.InvalidQuantity);
        }

        if (quantity > Reserved)
        {
            return Result.Failure(CatalogErrors.NothingToRelease);
        }

        Reserved -= quantity;
        return Result.Success();
    }

    /// <summary>Converts a reservation into a shipment: the units leave the warehouse.</summary>
    public Result Consume(int quantity)
    {
        if (quantity <= 0)
        {
            return Result.Failure(CatalogErrors.InvalidQuantity);
        }

        if (quantity > Reserved)
        {
            return Result.Failure(CatalogErrors.NothingToRelease);
        }

        Reserved -= quantity;
        OnHand -= quantity;
        return Result.Success();
    }

    /// <summary>Absolute correction from a stock take. Never drops below what is reserved.</summary>
    public Result AdjustOnHand(int newOnHand)
    {
        if (newOnHand < 0)
        {
            return Result.Failure(CatalogErrors.InvalidQuantity);
        }

        if (newOnHand < Reserved)
        {
            return Result.Failure(CatalogErrors.OnHandBelowReserved);
        }

        OnHand = newOnHand;
        return Result.Success();
    }
}
