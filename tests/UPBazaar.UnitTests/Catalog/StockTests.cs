using UPBazaar.Modules.Catalog.Domain;

namespace UPBazaar.UnitTests.Catalog;

public sealed class StockTests
{
    [Fact]
    public void Reserve_holds_units_without_removing_them_from_on_hand()
    {
        var stock = Stock.Create(onHand: 10);

        var result = stock.Reserve(3);

        result.IsSuccess.ShouldBeTrue();
        stock.OnHand.ShouldBe(10);
        stock.Reserved.ShouldBe(3);
        stock.Available.ShouldBe(7);
    }

    [Fact]
    public void Reserve_cannot_oversell_what_is_already_held()
    {
        var stock = Stock.Create(onHand: 5);
        stock.Reserve(4);

        var result = stock.Reserve(2);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CatalogErrors.InsufficientStock);
        stock.Reserved.ShouldBe(4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_rejects_non_positive_quantities(int quantity)
    {
        var result = Stock.Create(onHand: 10).Reserve(quantity);

        result.Error.ShouldBe(CatalogErrors.InvalidQuantity);
    }

    [Fact]
    public void Release_returns_held_units_to_the_available_pool()
    {
        var stock = Stock.Create(onHand: 10);
        stock.Reserve(4);

        stock.Release(4).IsSuccess.ShouldBeTrue();

        stock.Reserved.ShouldBe(0);
        stock.Available.ShouldBe(10);
    }

    [Fact]
    public void Release_cannot_exceed_what_is_reserved()
    {
        var stock = Stock.Create(onHand: 10);
        stock.Reserve(2);

        stock.Release(3).Error.ShouldBe(CatalogErrors.NothingToRelease);
    }

    [Fact]
    public void Consume_ships_reserved_units_out_of_the_warehouse()
    {
        var stock = Stock.Create(onHand: 10);
        stock.Reserve(3);

        stock.Consume(3).IsSuccess.ShouldBeTrue();

        stock.OnHand.ShouldBe(7);
        stock.Reserved.ShouldBe(0);
        stock.Available.ShouldBe(7);
    }

    [Fact]
    public void AdjustOnHand_refuses_to_drop_below_what_is_already_reserved()
    {
        var stock = Stock.Create(onHand: 10);
        stock.Reserve(6);

        stock.AdjustOnHand(4).Error.ShouldBe(CatalogErrors.OnHandBelowReserved);
        stock.OnHand.ShouldBe(10);
    }

    [Fact]
    public void AdjustOnHand_accepts_a_stock_take_that_still_covers_reservations()
    {
        var stock = Stock.Create(onHand: 10);
        stock.Reserve(6);

        stock.AdjustOnHand(8).IsSuccess.ShouldBeTrue();

        stock.OnHand.ShouldBe(8);
        stock.Available.ShouldBe(2);
    }
}
