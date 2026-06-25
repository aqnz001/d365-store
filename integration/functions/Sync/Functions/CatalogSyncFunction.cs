using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace PartsPortal.Functions.Sync;

/// <summary>
/// Timer/event-driven sync of catalog, inventory/ATP, price, customer and status
/// (TDD §3.2, §5; SAD §6.1). Cadence is config-driven per item class (TDD §7.3).
/// Catalog read path is BYOD-only (Golden Rule #3). Logic lands in T5/T10.
/// </summary>
public class CatalogSyncFunction
{
    // Placeholder cadence; real per-class cadence comes from configuration (TODO(T5)).
    [Function("CatalogSync")]
    public void Run([TimerTrigger("0 */15 * * * *")] TimerInfo timer, FunctionContext context)
    {
        var log = context.GetLogger<CatalogSyncFunction>();
        // TODO(T5): read BYOD delta → map (TDD §5.1) → upsert Shopify; publish ATP bands, never raw on-hand.
        log.LogInformation("Scaffolded catalog-sync tick.");
    }
}
