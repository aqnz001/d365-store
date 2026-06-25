namespace PartsPortal.Sync;

/// <summary>
/// Pure BYOD -> Shopify product mapping (TDD §5.1). Source rows come from the BYOD
/// replica only (browse data; never FinOps/OData — Golden Rule #3). Mapping is
/// deterministic and side-effect-free so it is trivially unit-testable and safe to
/// re-run for idempotent syncs (keyed by SKU = BYOD ItemNumber).
///
/// Field mapping (TDD §5.1):
///   ItemNumber          -> Sku
///   ProductName         -> Title
///   ProductDescription  -> BodyHtml
///   RetailCategory      -> ProductType
///   LifecycleState      -> Status      (see <see cref="ToShopifyStatus"/>)
///   BaseUnit / OrderMultiple / MinOrderQty / Backorderable -> Metafields
/// </summary>
public static class ProductMapper
{
    /// <summary>
    /// Lifecycle value that keeps a product listed (sellable) on the storefront.
    /// Any other value delists it (archived). Matched case-insensitively.
    /// </summary>
    private const string ActiveLifecycle = "Active";

    /// <summary>Maps a single BYOD catalog row to its Shopify upsert shape (TDD §5.1).</summary>
    public static ShopifyProductUpsert ToShopify(ByodProduct p)
    {
        ArgumentNullException.ThrowIfNull(p);

        return new ShopifyProductUpsert(
            Sku: p.ItemNumber,
            Title: p.ProductName,
            BodyHtml: p.ProductDescription,
            ProductType: p.RetailCategory,
            Status: ToShopifyStatus(p.LifecycleState),
            Metafields: new ShopifyProductMetafields(
                Unit: p.BaseUnit,
                OrderMultiple: p.OrderMultiple,
                MinOrderQty: p.MinOrderQty,
                Backorderable: p.Backorderable));
    }

    /// <summary>
    /// Maps a BYOD lifecycle state to a Shopify product status. "Active" (any casing)
    /// stays listed; anything else (e.g. "Discontinued") delists to "archived". This
    /// "delist by default" stance ensures a retired or unknown lifecycle never remains
    /// purchasable on the storefront.
    /// </summary>
    public static string ToShopifyStatus(string lifecycleState)
    {
        ArgumentNullException.ThrowIfNull(lifecycleState);

        return string.Equals(lifecycleState, ActiveLifecycle, StringComparison.OrdinalIgnoreCase)
            ? ShopifyProductStatus.Active
            : ShopifyProductStatus.Archived;
    }
}
