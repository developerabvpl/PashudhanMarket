using UPBazaar.Modules.Catalog.Contracts.Dtos;
using UPBazaar.Modules.Shipping.Contracts.Dtos;

namespace UPBazaar.Modules.Shipping.Application;

/// <summary>
/// Works out one box for the units of a part, from each product's packed size.
///
/// Stacking units as their packages were entered - footprint of the largest, height of all of
/// them added up - turned three small jars entered standing up into a 56 cm tower, and took the
/// length from one product and the breadth from another. Instead:
/// <list type="number">
/// <item>Every unit is laid flat: its shortest side goes up, whichever way the seller measured it.</item>
/// <item>The units stand in one or more stacks side by side. Each stack has the footprint of the
/// largest unit; units go, thickest first, onto the lowest stack.</item>
/// <item>Of one stack, two, and so on up to one per unit, the box whose sides add up to least is
/// chosen: the most compact box, since couriers cap and price length plus girth. A tie keeps
/// fewer stacks.</item>
/// </list>
/// Every step still errs large - a footprint as big as the largest unit, stacks as tall as the
/// tallest - which is the safe side: couriers surcharge a parcel bigger than booked, and never
/// refund one that turns out smaller. The sides come back longest first.
/// </summary>
public static class ParcelPacking
{
    /// <summary>The box for these products, each with how many units of it go in.</summary>
    public static ParcelDto Pack(IReadOnlyCollection<(ProductPackageDto Package, int Quantity)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var units = lines
            .SelectMany(l => Enumerable.Repeat(Flat(l.Package), Math.Max(l.Quantity, 0)))
            .ToList();

        if (units.Count == 0)
        {
            throw new ArgumentException("A parcel needs at least one unit.", nameof(lines));
        }

        var weight = lines.Sum(l => l.Package.WeightGrams * l.Quantity);
        var length = units.Max(u => u.Long);
        var breadth = units.Max(u => u.Middle);
        var thicknesses = units.Select(u => u.Short).OrderByDescending(t => t).ToList();

        ParcelDto? best = null;

        for (var stacks = 1; stacks <= units.Count; stacks++)
        {
            var box = Box(weight, length, breadth * stacks, TallestStack(thicknesses, stacks));

            if (best is null || SumOfSides(box) < SumOfSides(best))
            {
                best = box;
            }
        }

        return best!;
    }

    /// <summary>A unit's sides, longest first: laid flat, the last is its height in the box.</summary>
    private static (decimal Long, decimal Middle, decimal Short) Flat(ProductPackageDto package)
    {
        decimal[] sides = [package.LengthCm, package.BreadthCm, package.HeightCm];
        Array.Sort(sides);

        return (sides[2], sides[1], sides[0]);
    }

    /// <summary>How tall the tallest stack is when units go, thickest first, onto the lowest of <paramref name="stacks"/>.</summary>
    private static decimal TallestStack(List<decimal> thicknessesDescending, int stacks)
    {
        var heights = new decimal[stacks];

        foreach (var thickness in thicknessesDescending)
        {
            var lowest = Array.IndexOf(heights, heights.Min());
            heights[lowest] += thickness;
        }

        return heights.Max();
    }

    private static ParcelDto Box(int weight, decimal a, decimal b, decimal c)
    {
        decimal[] sides = [a, b, c];
        Array.Sort(sides);

        return new ParcelDto(weight, sides[2], sides[1], sides[0]);
    }

    private static decimal SumOfSides(ParcelDto box) => box.LengthCm + box.BreadthCm + box.HeightCm;
}
