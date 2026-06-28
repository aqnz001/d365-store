using System.Net;
using System.Net.Http.Json;

namespace PartsPortal.Bff.Clients;

/// <summary>Cart-validation metafields synced from BYOD (TDD §5.1).</summary>
public sealed record CatalogProductMetafields(string Unit, decimal OrderMultiple, decimal MinOrderQty, bool Backorderable);

/// <summary>
/// A storefront catalog product (master + metafields). Availability is resolved live, not here.
/// <see cref="ListPrice"/> is the indicative list price (a catalog attribute); the customer's
/// contract net price is resolved live by the pricing service at the checkout gate.
/// </summary>
public sealed record CatalogProduct(
    string Sku,
    string Title,
    string BodyHtml,
    string ProductType,
    string Status,
    CatalogProductMetafields Metafields,
    decimal? ListPrice = null,
    string? AvailabilityBand = null);

/// <summary>Reads the storefront catalog (BYOD-synced; never FinOps/OData — Golden Rule #3).</summary>
public interface ICatalogApi
{
    Task<IReadOnlyList<CatalogProduct>> ListAsync(CancellationToken ct = default);

    Task<CatalogProduct?> GetAsync(string sku, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class CatalogApi(IHttpClientFactory httpClientFactory) : ICatalogApi
{
    public async Task<IReadOnlyList<CatalogProduct>> ListAsync(CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(BffClients.Catalog);
        var response = await client.GetFromJsonAsync<CatalogListResponse>("admin/products", ct);
        return response?.Products ?? [];
    }

    public async Task<CatalogProduct?> GetAsync(string sku, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(BffClients.Catalog);
        using var response = await client.GetAsync($"admin/products/{Uri.EscapeDataString(sku)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CatalogProduct>(ct);
    }

    private sealed record CatalogListResponse(IReadOnlyList<CatalogProduct> Products, int Count);
}
