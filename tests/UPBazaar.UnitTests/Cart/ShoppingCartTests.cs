using UPBazaar.Modules.Cart.Domain;

namespace UPBazaar.UnitTests.Cart;

public sealed class ShoppingCartTests
{
    [Fact]
    public void Setting_zero_removes_the_line()
    {
        var cart = ShoppingCart.Create(Guid.NewGuid());
        var product = Guid.NewGuid();

        cart.SetQuantity(product, 3, 10m);
        cart.SetQuantity(product, 0, 0m);

        cart.Lines.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ShoppingCart.MaxQuantityPerLine + 1)]
    public void A_quantity_outside_the_limits_is_refused(int quantity)
    {
        ShoppingCart.Create(Guid.NewGuid()).SetQuantity(Guid.NewGuid(), quantity, 10m).Error
            .ShouldBe(CartErrors.QuantityOutOfRange);
    }

    [Fact]
    public void A_full_cart_takes_no_new_product_but_still_changes_existing_ones()
    {
        var cart = ShoppingCart.Create(Guid.NewGuid());
        var first = Guid.NewGuid();

        cart.SetQuantity(first, 1, 1m);

        for (var i = 1; i < ShoppingCart.MaxLines; i++)
        {
            cart.SetQuantity(Guid.NewGuid(), 1, 1m);
        }

        cart.SetQuantity(Guid.NewGuid(), 1, 1m).Error.ShouldBe(CartErrors.TooManyLines);
        cart.SetQuantity(first, 5, 1m).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Merging_adds_to_what_is_there_and_caps_instead_of_failing()
    {
        var cart = ShoppingCart.Create(Guid.NewGuid());
        var product = Guid.NewGuid();

        cart.SetQuantity(product, 70, 10m);

        cart.Merge(product, 70, 10m).ShouldBeTrue();
        cart.Lines.Single().Quantity.ShouldBe(ShoppingCart.MaxQuantityPerLine);
    }

    [Fact]
    public void Choosing_a_quantity_again_accepts_the_current_price()
    {
        var cart = ShoppingCart.Create(Guid.NewGuid());
        var product = Guid.NewGuid();

        cart.SetQuantity(product, 1, 100m);
        cart.SetQuantity(product, 2, 110m);

        cart.Lines.Single().PriceWhenAdded.ShouldBe(110m);
    }
}
