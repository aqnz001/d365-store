namespace PartsPortal.Bff;

/// <summary>
/// Config-driven BFF settings (Golden Rule #1). Base URLs point at APIM/middleware and the
/// storefront catalog; swapped for sandbox/prod with no code change.
/// </summary>
public sealed class BffOptions
{
    public const string SectionName = "Bff";

    /// <summary>Base URL of the middleware (via APIM) for /cart/*, /pricing/resolve, /order.</summary>
    public string MiddlewareBaseUrl { get; set; } = string.Empty;

    /// <summary>Base URL of the storefront catalog store (BYOD-synced; never FinOps/OData — Golden Rule #3).</summary>
    public string CatalogBaseUrl { get; set; } = string.Empty;

    /// <summary>Allowed SPA origin for CORS (the React app).</summary>
    public string SpaOrigin { get; set; } = string.Empty;
}
