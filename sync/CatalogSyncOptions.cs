namespace PartsPortal.Sync;

/// <summary>Which BYOD catalog source the sync reads from (DR-005, Golden Rule #1).</summary>
public enum CatalogSourceMode
{
    /// <summary>Phase-1 deterministic embedded sample catalog (default; dev/test).</summary>
    Sample,

    /// <summary>Phase-2 Azure SQL BYOD replica of the D365 product master.</summary>
    Sql,
}

/// <summary>Config-driven catalog-sync settings — source selection + BYOD SQL connection.</summary>
public sealed class CatalogSyncOptions
{
    public const string SectionName = "CatalogSync";

    /// <summary>Catalog source implementation. Defaults to the embedded sample.</summary>
    public CatalogSourceMode SourceMode { get; set; } = CatalogSourceMode.Sample;

    /// <summary>BYOD Azure SQL settings (used when <see cref="SourceMode"/> is <c>Sql</c>).</summary>
    public ByodSqlOptions Byod { get; set; } = new();
}

/// <summary>Azure SQL BYOD replica connection + query (Phase 2).</summary>
public sealed class ByodSqlOptions
{
    /// <summary>
    /// Connection string to the BYOD replica (Key Vault). For managed-identity auth set
    /// <c>Authentication=Active Directory Default</c> in the string — no code change.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Query returning one row per catalog item with the columns the mapper reads (ItemNumber,
    /// ProductName, ProductDescription, RetailCategory, BaseUnit, OrderMultiple, MinOrderQty,
    /// Backorderable, LifecycleState). Override with the customer's actual BYOD view/table.
    /// </summary>
    public string Query { get; set; } =
        "SELECT ItemNumber, ProductName, ProductDescription, RetailCategory, BaseUnit, " +
        "OrderMultiple, MinOrderQty, Backorderable, LifecycleState FROM dbo.StorefrontCatalog";
}
