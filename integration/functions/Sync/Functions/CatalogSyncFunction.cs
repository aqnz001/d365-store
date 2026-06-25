using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PartsPortal.Sync;

namespace PartsPortal.Functions.Sync;

/// <summary>
/// Timer-driven catalog sync: reads the BYOD catalog and upserts it into Shopify
/// (TDD §3.2, §5.1; SAD §6.1). Catalog read path is BYOD-only (Golden Rule #3); it
/// publishes product master + metafields, never stock counts (Golden Rule #4).
/// Inventory/price/customer/status sync land in later tasks (T10).
/// </summary>
public class CatalogSyncFunction(CatalogSyncJob catalogSync)
{
    // Placeholder cadence; real per-class cadence comes from configuration (TODO(T10)).
    [Function("CatalogSync")]
    public async Task Run([TimerTrigger("0 */15 * * * *")] TimerInfo timer, FunctionContext context)
    {
        var log = context.GetLogger<CatalogSyncFunction>();
        var result = await catalogSync.RunAsync(context.CancellationToken);
        log.LogInformation(
            "Catalog sync: read {Read}, upserted {Upserted}, delisted {Delisted}.",
            result.Read, result.Upserted, result.Delisted);
    }
}
