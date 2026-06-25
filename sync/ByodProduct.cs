namespace PartsPortal.Sync;

/// <summary>
/// A catalog row read from the BYOD replica (TDD §5.1 source side). Browse/catalog data
/// comes from BYOD only — never FinOps/OData (Golden Rule #3). These are the fields the
/// storefront needs; availability is NOT here (that is IVS/ATP, resolved separately).
/// </summary>
public sealed record ByodProduct(
    string ItemNumber,
    string ProductName,
    string ProductDescription,
    string RetailCategory,
    string BaseUnit,
    decimal OrderMultiple,
    decimal MinOrderQty,
    bool Backorderable,
    string LifecycleState);
