namespace PartsPortal.Sync;

/// <summary>
/// BYOD -> Shopify catalog sync job (T5).
///
/// Pipeline:
///   1. Read catalog/product data from BYOD (never FinOps/OData for browse data).
///   2. Map to the Shopify product/variant shape per TDD §5.1.
///   3. Upsert into Shopify.
///
/// Invariants:
///   - Publishes only advisory availability *bands*; never raw on-hand or exact
///     stock counts. The live availability check (IVS) at the checkout gate
///     remains authoritative; synced stock is advisory only.
///   - Idempotent re-runs: a repeated sync produces no duplicate products and
///     no spurious changes (key on stable BYOD identifiers).
///   - Endpoints, batch sizes, and band thresholds come from configuration,
///     not literals.
///
/// May later be hosted/triggered by the Sync Azure Function. No real logic yet.
/// </summary>
public sealed class CatalogSyncJob
{
    // TODO(T5): inject BYOD reader, Shopify upsert client, and band mapper
    //           (all config-driven; secrets via Key Vault + managed identity).

    // TODO(T5): RunAsync — read BYOD batch -> map (TDD §5.1) -> upsert to Shopify,
    //           emitting advisory bands only and de-duping for idempotent re-runs.
}
