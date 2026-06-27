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
    // Cadence is config-driven via the CatalogSync:Schedule app setting (default 0 */15 * * * *,
    // provided by IaC/app settings at deploy and local.settings.json for local `func start`).
    [Function("CatalogSync")]
    public async Task Run([TimerTrigger("%CatalogSync:Schedule%")] TimerInfo timer, FunctionContext context)
    {
        var log = context.GetLogger<CatalogSyncFunction>();
        var result = await catalogSync.RunAsync(context.CancellationToken);
        log.LogInformation(
            "Catalog sync: read {Read}, upserted {Upserted}, delisted {Delisted}.",
            result.Read, result.Upserted, result.Delisted);
    }
}
