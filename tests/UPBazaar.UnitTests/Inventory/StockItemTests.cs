using UPBazaar.Modules.Inventory.Contracts.Events;
using UPBazaar.Modules.Inventory.Domain;

namespace UPBazaar.UnitTests.Inventory;

public sealed class StockItemTests
{
    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Every_change_is_written_to_the_ledger_with_the_resulting_figure()
    {
        var item = StockItem.Create(Guid.NewGuid());

        item.Receive(10, "Delivery", "DN-1", Now, "staff-1");
        item.WriteOff(2, "Crushed in transit", Now, "staff-1");
        item.Count(7, "Monthly count", Now, "staff-1");

        item.OnHandQuantity.ShouldBe(7);
        item.Movements.Select(m => (m.Type, m.OnHandChange, m.OnHandAfter)).ShouldBe(
        [
            (StockMovementType.Received, 10, 10),
            (StockMovementType.WrittenOff, -2, 8),
            (StockMovementType.Counted, -1, 7),
        ]);
    }

    [Fact]
    public void Reserving_more_than_is_available_is_refused_and_changes_nothing()
    {
        var item = Stocked(5);
        item.Reserve(3, "order-1", Now);

        item.Reserve(3, "order-2", Now).Error.ShouldBe(InventoryErrors.InsufficientStock);

        item.ReservedQuantity.ShouldBe(3);
        item.AvailableQuantity.ShouldBe(2);
    }

    [Fact]
    public void Committing_removes_the_goods_and_releasing_puts_them_back()
    {
        var item = Stocked(10);
        item.Reserve(4, "order-1", Now);
        item.Reserve(3, "order-2", Now);

        item.Commit(4, "order-1", Now);
        item.Release(3, "order-2", Now);

        item.OnHandQuantity.ShouldBe(6);
        item.ReservedQuantity.ShouldBe(0);
        item.AvailableQuantity.ShouldBe(6);
    }

    [Fact]
    public void A_count_or_write_off_cannot_take_stock_that_is_already_held()
    {
        var item = Stocked(5);
        item.Reserve(4, "order-1", Now);

        item.Count(3, null, Now, "staff-1").Error.ShouldBe(InventoryErrors.BelowReserved);
        item.WriteOff(2, "Damaged", Now, "staff-1").Error.ShouldBe(InventoryErrors.BelowReserved);
        item.OnHandQuantity.ShouldBe(5);
    }

    [Fact]
    public void Selling_the_last_unit_announces_depletion_and_restocking_announces_its_return()
    {
        var item = Stocked(1);

        item.Reserve(1, "order-1", Now);
        item.DomainEvents.OfType<StockDepletedDomainEvent>().Count().ShouldBe(1);

        item.ClearDomainEvents();
        item.Receive(5, null, null, Now, "staff-1");

        item.DomainEvents.OfType<StockReplenishedDomainEvent>().Single().AvailableQuantity.ShouldBe(5);
    }

    [Fact]
    public void Movements_that_do_not_cross_zero_announce_nothing()
    {
        var item = Stocked(10);

        item.Reserve(2, "order-1", Now);
        item.Receive(3, null, null, Now, "staff-1");

        item.DomainEvents.ShouldBeEmpty();
    }

    private static StockItem Stocked(int quantity)
    {
        var item = StockItem.Create(Guid.NewGuid());
        item.Receive(quantity, null, null, Now, "staff-1");
        item.ClearDomainEvents();

        return item;
    }
}
