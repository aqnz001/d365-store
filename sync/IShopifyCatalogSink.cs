namespace PartsPortal.Sync;

/// <summary>
/// Upserts a mapped product into Shopify, keyed by SKU (idempotent re-runs produce no
/// duplicates). Phase 1 targets the Shopify mock (shopify-sim); Phase 2 swaps in the real
/// Shopify Admin API behind this same interface (Golden Rule #1).
/// </summary>
public interface IShopifyCatalogSink
{
    Task UpsertAsync(ShopifyProductUpsert product, CancellationToken ct = default);
}
