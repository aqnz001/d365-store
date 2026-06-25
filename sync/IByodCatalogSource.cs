namespace PartsPortal.Sync;

/// <summary>
/// Reads the catalog from the BYOD replica. Phase 1 uses a deterministic sample source;
/// Phase 2 swaps in an Azure SQL (BYOD) implementation behind this same interface with no
/// caller change (Golden Rule #1).
/// </summary>
public interface IByodCatalogSource
{
    Task<IReadOnlyList<ByodProduct>> ReadCatalogAsync(CancellationToken ct = default);
}
