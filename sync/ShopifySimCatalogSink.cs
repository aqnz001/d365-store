using System.Net.Http.Json;
using PartsPortal.Shared.Http;

namespace PartsPortal.Sync;

/// <summary>
/// Upserts mapped products into Shopify via the config-driven, resilient "shopify" HttpClient
/// (T4; retry/backoff). Phase 1 targets the shopify-sim mock; Phase 2 swaps in the real
/// Shopify Admin API behind <see cref="IShopifyCatalogSink"/> with no caller change.
/// Upsert is keyed by SKU, so re-running the sync never creates duplicates.
/// </summary>
public sealed class ShopifySimCatalogSink(IHttpClientFactory httpClientFactory) : IShopifyCatalogSink
{
    public async Task UpsertAsync(ShopifyProductUpsert product, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        var client = httpClientFactory.CreateClient(ResilientHttpClientExtensions.ShopifyClient);
        using var response = await client.PutAsJsonAsync(
            $"admin/products/{Uri.EscapeDataString(product.Sku)}", product, ct);
        response.EnsureSuccessStatusCode();
    }
}
