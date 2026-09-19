using UPBazaar.Modules.Catalog.Contracts.Events;
using UPBazaar.Modules.Catalog.Domain;

namespace UPBazaar.UnitTests.Catalog;

public sealed class ProductTests
{
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

        product.UpdateDetails(product.Name, null, null, 150m, Diyas);
        product.DomainEvents.ShouldBeEmpty();

        product.Publish();
        product.UpdateDetails(product.Name, null, null, 175m, Diyas);

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
        product.UpdateDetails("New", null, null, 1m, Diyas).Error.ShouldBe(CatalogErrors.ProductArchived);
        product.SetStock(5).Error.ShouldBe(CatalogErrors.ProductArchived);
    }

    [Fact]
    public void Stock_cannot_go_negative()
    {
        NewProduct().SetStock(-1).Error.ShouldBe(CatalogErrors.StockBelowReserved);
    }

    [Theory]
    [InlineData("Maa Agarbatti Gulab  Cow-Dung Dhup Stick 1kg!", "maa-agarbatti-gulab-cow-dung-dhup-stick-1kg")]
    [InlineData("गोबर दीया", "upb-diy-001")]
    public void The_slug_is_readable_ascii_and_never_empty(string name, string expected)
    {
        var product = Product.CreateDraft("UPB-DIY-001", name, null, null, 10m, Guid.NewGuid(), Diyas, 0);

        product.Slug.ShouldBe(expected);
    }

    private static Product NewProduct() =>
        Product.CreateDraft("UPB-DIY-001", "Cow Dung Diya, pack of 12", null, null, 120m, Guid.NewGuid(), Diyas, 10);
}
