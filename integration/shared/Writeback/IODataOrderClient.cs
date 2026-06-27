namespace PartsPortal.Shared.Writeback;

/// <summary>
/// FinOps OData sales-order writeback (TDD §4.4): header first, then lines (FK = order number).
/// Maps 503 → <see cref="TransientWritebackException"/> and 4xx → <see cref="PermanentWritebackException"/>.
/// Phase 1 talks to odata-sim; Phase 2 to FinOps OData, behind this same interface.
/// </summary>
public interface IODataOrderClient
{
    /// <summary>Creates the sales order header; returns the FinOps-generated order number.</summary>
    Task<string> CreateHeaderAsync(string customerAccount, CancellationToken ct = default);

    Task CreateLineAsync(string salesOrderNumber, string itemNumber, decimal quantity, CancellationToken ct = default);

    /// <summary>
    /// Current FinOps price for an item (trade agreement, as of now), or <c>null</c> when none is
    /// on record. Used by writeback price-integrity (TDD §9) to compare against the locked price.
    /// </summary>
    Task<decimal?> GetCurrentPriceAsync(string itemNumber, CancellationToken ct = default);
}
