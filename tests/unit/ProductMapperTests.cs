using PartsPortal.Sync;
using Xunit;

namespace PartsPortal.Tests.Unit;

/// <summary>
/// Unit tests for the BYOD -> Shopify product mapper (TDD §5.1). Deterministic and
/// IO-free: assert exact field mapping, the lifecycle -> status "delist by default"
/// rule (Active stays listed; anything else archives), case-insensitivity of "active",
/// metafield carry-through, and null-argument guarding.
/// </summary>
public class ProductMapperTests
{
    // Deliberately distinct, non-overlapping field values so a mis-wired mapping
    // (e.g. Title <- ProductDescription) cannot accidentally satisfy an assertion.
    private static ByodProduct NewProduct(string lifecycle = "Active") => new(
        ItemNumber: "ITEM-001",
        ProductName: "Brake Caliper",
        ProductDescription: "<p>Heavy-duty brake caliper</p>",
        RetailCategory: "Braking",
        BaseUnit: "EA",
        OrderMultiple: 4m,
        MinOrderQty: 8m,
        Backorderable: true,
        LifecycleState: lifecycle);

    [Fact]
    public void ToShopify_MapsEveryScalarField()
    {
        var p = NewProduct();

        var result = ProductMapper.ToShopify(p);

        Assert.Equal(p.ItemNumber, result.Sku);
        Assert.Equal(p.ProductName, result.Title);
        Assert.Equal(p.ProductDescription, result.BodyHtml);
        Assert.Equal(p.RetailCategory, result.ProductType);
    }

    [Fact]
    public void ToShopify_CarriesAllMetafields()
    {
        var p = NewProduct();

        var result = ProductMapper.ToShopify(p);

        Assert.NotNull(result.Metafields);
        Assert.Equal(p.BaseUnit, result.Metafields.Unit);
        Assert.Equal(p.OrderMultiple, result.Metafields.OrderMultiple);
        Assert.Equal(p.MinOrderQty, result.Metafields.MinOrderQty);
        Assert.Equal(p.Backorderable, result.Metafields.Backorderable);
    }

    [Fact]
    public void ToShopify_ActiveLifecycle_MapsToActiveStatus()
    {
        var result = ProductMapper.ToShopify(NewProduct("Active"));

        Assert.Equal(ShopifyProductStatus.Active, result.Status);
        Assert.Equal("active", result.Status);
    }

    [Fact]
    public void ToShopify_DiscontinuedLifecycle_MapsToArchivedStatus()
    {
        var result = ProductMapper.ToShopify(NewProduct("Discontinued"));

        Assert.Equal(ShopifyProductStatus.Archived, result.Status);
        Assert.Equal("archived", result.Status);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("ACTIVE")]
    [InlineData("Active")]
    [InlineData("AcTiVe")]
    public void ToShopifyStatus_ActiveIsCaseInsensitive(string lifecycle)
    {
        Assert.Equal(ShopifyProductStatus.Active, ProductMapper.ToShopifyStatus(lifecycle));
    }

    [Theory]
    [InlineData("Discontinued")]
    [InlineData("discontinued")]
    [InlineData("Retired")]
    [InlineData("Draft")]
    [InlineData("")]
    [InlineData(" Active ")] // padding is not "Active" — delist by default
    public void ToShopifyStatus_NonActiveDelistsByDefault(string lifecycle)
    {
        Assert.Equal(ShopifyProductStatus.Archived, ProductMapper.ToShopifyStatus(lifecycle));
    }

    [Fact]
    public void ToShopify_NullProduct_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ProductMapper.ToShopify(null!));
    }

    [Fact]
    public void ToShopify_NullLifecycle_Throws()
    {
        var p = NewProduct(lifecycle: null!);

        Assert.Throws<ArgumentNullException>(() => ProductMapper.ToShopify(p));
    }

    [Fact]
    public void ToShopifyStatus_NullLifecycle_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ProductMapper.ToShopifyStatus(null!));
    }
}
