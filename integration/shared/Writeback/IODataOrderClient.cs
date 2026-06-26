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
}
