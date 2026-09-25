using UPBazaar.Modules.Catalog.Contracts.Events;
using UPBazaar.Modules.Catalog.Domain;

namespace UPBazaar.UnitTests.Catalog;

public sealed class ProductTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Category Diyas = Category.Create("Cow Dung Diya", parent: null);

    [Fact]
    public void A_new_product_is_a_draft_and_raises_nothing()
    {
        var product = NewProduct();

        product.Status.ShouldBe(ProductStatus.Draft);
        product.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Publishing_raises_one_event_and_publishing_again_changes_nothing()
    {
        var product = NewProduct();

        product.Publish().IsSuccess.ShouldBeTrue();
        product.Publish().IsSuccess.ShouldBeTrue();

        product.Status.ShouldBe(ProductStatus.Active);
        product.DomainEvents.OfType<ProductPublishedDomainEvent>().Count().ShouldBe(1);
    }

    [Fact]
    public void Only_a_published_price_change_is_announced()
    {
        var product = NewProduct();

        product.UpdateDetails(product.Name, null, null, 150m, Diyas, Now);
        product.DomainEvents.ShouldBeEmpty();

        product.Publish();
        product.UpdateDetails(product.Name, null, null, 175m, Diyas, Now);

        var changed = product.DomainEvents.OfType<ProductPriceChangedDomainEvent>().Single();
        changed.OldPrice.ShouldBe(150m);
        changed.NewPrice.ShouldBe(175m);
    }

    [Fact]
    public void An_archived_product_is_frozen()
    {
        var product = NewProduct();
        product.Archive();

        product.Publish().Error.ShouldBe(CatalogErrors.ProductArchived);
        product.UpdateDetails("New", null, null, 1m, Diyas, Now).Error.ShouldBe(CatalogErrors.ProductArchived);
    }

    [Theory]
    [InlineData("Maa Agarbatti Gulab  Cow-Dung Dhup Stick 1kg!", "maa-agarbatti-gulab-cow-dung-dhup-stick-1kg")]
    [InlineData("गोबर दीया", "upb-diy-001")]
    public void The_slug_is_readable_ascii_and_never_empty(string name, string expected)
    {
        var product = Product.CreateDraft("UPB-DIY-001", name, null, null, 10m, Guid.NewGuid(), Diyas);

        product.Slug.ShouldBe(expected);
    }

    [Fact]
    public void A_sale_price_applies_only_while_the_sale_runs()
    {
        var product = NewProduct();

        product.SetSale(99m, Now.AddDays(1), Now.AddDays(3), Now).IsSuccess.ShouldBeTrue();

        product.PriceAt(Now).ShouldBe(120m);
        product.PriceAt(Now.AddDays(1)).ShouldBe(99m);
        product.PriceAt(Now.AddDays(3)).ShouldBe(120m);
        Product.PriceAt(120m, 99m, Now.AddDays(1), Now.AddDays(3), Now.AddDays(2)).ShouldBe(99m);

        product.EndSale();

        product.PriceAt(Now.AddDays(2)).ShouldBe(120m);
        product.SalePrice.ShouldBeNull();
    }

    [Theory]
    [InlineData(120, 1)]
    [InlineData(0, 1)]
    public void A_sale_price_must_be_below_the_regular_price(decimal salePrice, int days)
    {
        NewProduct().SetSale(salePrice, Now, Now.AddDays(days), Now).Error.ShouldBe(CatalogErrors.SaleNotBelowPrice);
    }

    [Fact]
    public void A_sale_must_end_after_it_starts_and_not_in_the_past()
    {
        var product = NewProduct();

        product.SetSale(99m, Now, Now, Now).Error.ShouldBe(CatalogErrors.SaleEndsTooSoon);
        product.SetSale(99m, Now.AddDays(-3), Now.AddDays(-1), Now).Error.ShouldBe(CatalogErrors.SaleEndsTooSoon);
    }

    [Fact]
    public void The_regular_price_stays_above_a_sale_until_it_ends()
    {
        var product = NewProduct();
        product.SetSale(99m, Now, Now.AddDays(3), Now);

        product.Reprice(99m, Now).Error.ShouldBe(CatalogErrors.PriceNotAboveSale);
        product.UpdateDetails(product.Name, null, null, 90m, Diyas, Now).Error.ShouldBe(CatalogErrors.PriceNotAboveSale);
        product.Reprice(110m, Now).IsSuccess.ShouldBeTrue();

        product.Reprice(50m, Now.AddDays(3)).IsSuccess.ShouldBeTrue();
        product.SalePrice.ShouldBeNull();
    }

    private static Product NewProduct() =>
        Product.CreateDraft("UPB-DIY-001", "Cow Dung Diya, pack of 12", null, null, 120m, Guid.NewGuid(), Diyas);
}
