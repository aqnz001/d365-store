using Microsoft.Extensions.Logging;

namespace PartsPortal.Sync;

/// <summary>
/// BYOD -> Shopify catalog sync job (T5, TDD §5.1).
///
/// Pipeline: read the catalog from BYOD (never FinOps/OData for browse — Golden Rule #3)
/// -> map each row to the Shopify shape (<see cref="ProductMapper"/>) -> upsert into Shopify.
///
/// Invariants:
///   - Upsert is keyed by SKU, so repeated runs are idempotent (no duplicate products).
///   - Lifecycle drives delisting: a discontinued BYOD row maps to status "archived".
///   - Carries product master + cart-validation metafields only; it publishes NO stock
///     counts. Availability stays advisory bands via IVS at the checkout gate (Golden Rule #4).
///   - Endpoints come from configuration (Golden Rule #1).
/// </summary>
public sealed class CatalogSyncJob(
    IByodCatalogSource source,
    IShopifyCatalogSink sink,
    ILogger<CatalogSyncJob> logger)
{
    public async Task<CatalogSyncResult> RunAsync(CancellationToken ct = default)
    {
        var products = await source.ReadCatalogAsync(ct);

        var upserted = 0;
        var delisted = 0;
        foreach (var product in products)
        {
            var mapped = ProductMapper.ToShopify(product);
            await sink.UpsertAsync(mapped, ct);
            upserted++;
            if (mapped.Status == ShopifyProductStatus.Archived)
            {
                delisted++;
            }
        }

        logger.LogInformation(
            "Catalog sync complete: {Upserted} upserted ({Delisted} delisted) from {Read} BYOD rows.",
            upserted, delisted, products.Count);

        return new CatalogSyncResult(products.Count, upserted, delisted);
    }
}

/// <summary>Outcome of a catalog sync run.</summary>
public sealed record CatalogSyncResult(int Read, int Upserted, int Delisted);
