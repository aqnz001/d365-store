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

    /// <summary>
    /// API key the BFF presents to the middleware: an APIM subscription key (default header
    /// <c>Ocp-Apim-Subscription-Key</c>) or an Azure Functions key. Sourced from Key Vault.
    /// Empty against the dev-gateway (no key needed).
    /// </summary>
    public string MiddlewareApiKey { get; set; } = string.Empty;

    /// <summary>Header the middleware key is sent in (APIM = Ocp-Apim-Subscription-Key; Functions = x-functions-key).</summary>
    public string MiddlewareApiKeyHeader { get; set; } = "Ocp-Apim-Subscription-Key";

    /// <summary>Optional API key for the catalog store (header <see cref="CatalogApiKeyHeader"/>), if it requires one.</summary>
    public string CatalogApiKey { get; set; } = string.Empty;

    /// <summary>Header the catalog key is sent in.</summary>
    public string CatalogApiKeyHeader { get; set; } = "Ocp-Apim-Subscription-Key";
}
