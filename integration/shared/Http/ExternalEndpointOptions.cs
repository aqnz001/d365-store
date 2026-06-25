namespace PartsPortal.Shared.Http;

/// <summary>
/// Config-driven base URLs for external systems (Golden Rule #1: endpoints are
/// configurable so Phase 2 swaps mocks → sandbox with no code change). Bound from
/// configuration; never hardcoded. Resilient clients (retry + backoff) are wired
/// over these in T4 via Microsoft.Extensions.Http.Resilience.
/// </summary>
public sealed class ExternalEndpointOptions
{
    public const string SectionName = "ExternalEndpoints";

    /// <summary>Inventory Visibility Service base URL (mock now, sandbox in Phase 2).</summary>
    public string IvsBaseUrl { get; set; } = string.Empty;

    /// <summary>FinOps OData base URL (mock now).</summary>
    public string ODataBaseUrl { get; set; } = string.Empty;

    /// <summary>Pricing/credit custom service base URL (mock now).</summary>
    public string PricingCreditBaseUrl { get; set; } = string.Empty;

    /// <summary>Shopify catalog target base URL (shopify-sim mock now, dev store in Phase 2).</summary>
    public string ShopifyBaseUrl { get; set; } = string.Empty;
}
