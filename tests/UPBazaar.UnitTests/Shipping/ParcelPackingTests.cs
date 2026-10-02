using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Application;
using UPBazaar.Modules.Shipping.Contracts.Dtos;

namespace UPBazaar.UnitTests.Shipping;

/// <summary>The box suggested when packing: units laid flat, in compact stacks, erring large.</summary>
public sealed class ParcelPackingTests
{
    [Fact]
    public void One_unit_ships_in_its_own_package_longest_side_first()
    {
        ParcelPacking.Pack([(Package(200, 5m, 12m, 8m), 1)]).ShouldBe(new ParcelDto(200, 12m, 8m, 5m));
    }

    [Fact]
    public void Units_measured_standing_up_are_laid_flat_rather_than_stacked_into_a_tower()
    {
        // Three jars entered 10 x 10 x 30 standing: stacked as entered they made a 90 cm tower.
        var parcel = ParcelPacking.Pack([(Package(300, 10m, 10m, 30m), 3)]);

        parcel.ShouldBe(new ParcelDto(900, 30m, 30m, 10m));
        (parcel.LengthCm * parcel.BreadthCm * parcel.HeightCm).ShouldBe(9000m);
    }

    [Fact]
    public void The_footprint_is_the_largest_unit_and_flat_units_stack_on_it()
    {
        var parcel = ParcelPacking.Pack([(Package(250, 20m, 15m, 5m), 2), (Package(120, 10m, 8m, 4m), 1)]);

        parcel.ShouldBe(new ParcelDto(620, 20m, 15m, 14m));
    }

    [Fact]
    public void Lengths_and_breadths_of_differently_measured_products_are_not_mixed()
    {
        // Entered one long and one wide, both are 30 x 25 lying flat: one 30 x 25 footprint, not a 30 x 25 from each.
        var parcel = ParcelPacking.Pack([(Package(100, 30m, 4m, 25m), 1), (Package(100, 4m, 25m, 30m), 1)]);

        parcel.ShouldBe(new ParcelDto(200, 30m, 25m, 8m));
    }

    [Fact]
    public void A_tall_stack_is_split_into_stacks_side_by_side_when_that_is_more_compact()
    {
        // Twelve 10 x 10 x 5 tins: one stack is 60 cm tall; two stacks of six make a 30 x 20 x 10 box.
        var parcel = ParcelPacking.Pack([(Package(50, 10m, 10m, 5m), 12)]);

        parcel.ShouldBe(new ParcelDto(600, 30m, 20m, 10m));
        (parcel.LengthCm + parcel.BreadthCm + parcel.HeightCm).ShouldBeLessThan(10m + 10m + 60m);
    }

    [Fact]
    public void An_empty_part_has_no_parcel()
    {
        Should.Throw<ArgumentException>(() => ParcelPacking.Pack([]));
    }

    private static ProductPackageDto Package(int grams, decimal length, decimal breadth, decimal height) =>
        new(grams, length, breadth, height);
}
