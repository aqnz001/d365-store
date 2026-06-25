namespace PartsPortal.Sync;

/// <summary>
/// The Shopify-facing product shape a BYOD row maps to (TDD §5.1 target side). Keyed by
/// SKU (= BYOD ItemNumber) so upserts are idempotent. Status drives delisting on lifecycle
/// change (discontinued -> archived). Metafields carry the B2B cart-validation inputs.
/// </summary>
public sealed record ShopifyProductUpsert(
    string Sku,
    string Title,
    string BodyHtml,
    string ProductType,
    string Status,
    ShopifyProductMetafields Metafields);

/// <summary>Cart-validation metafields synced alongside the product (TDD §5.1, §6.8).</summary>
public sealed record ShopifyProductMetafields(
    string Unit,
    decimal OrderMultiple,
    decimal MinOrderQty,
    bool Backorderable);

/// <summary>Canonical Shopify product status values (delisting = archived).</summary>
public static class ShopifyProductStatus
{
    public const string Active = "active";
    public const string Archived = "archived";
}
