using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PartsPortal.Bff;
using PartsPortal.Bff.Auth;
using PartsPortal.Bff.Clients;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// S1+S2 — the BFF hosted in-process: anonymous health, dev-auth identity, and catalog browse
/// (with the catalog client stubbed). Confirms auth + the catalog endpoints wire up.
/// </summary>
public class BffCatalogTests(WebApplicationFactory<BffApp> factory) : IClassFixture<WebApplicationFactory<BffApp>>
{
    private sealed class FakeCatalogApi(IReadOnlyList<CatalogProduct> products) : ICatalogApi
    {
        public Task<IReadOnlyList<CatalogProduct>> ListAsync(CancellationToken ct = default) => Task.FromResult(products);

        public Task<CatalogProduct?> GetAsync(string sku, CancellationToken ct = default) =>
            Task.FromResult(products.FirstOrDefault(p => p.Sku == sku));
    }

    private HttpClient ClientWithCatalog(params CatalogProduct[] products) =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<ICatalogApi>(_ => new FakeCatalogApi(products)))).CreateClient();

    [Fact]
    public async Task Health_is_healthy()
    {
        var response = await factory.CreateClient().GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Me_returns_the_authenticated_customer()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthenticationHandler.CustomerHeader, "C-1001");

        var doc = await client.GetFromJsonAsync<JsonElement>("/api/me");

        Assert.Equal("C-1001", doc.GetProperty("customerAccount").GetString());
    }

    [Fact]
    public async Task Catalog_list_returns_products()
    {
        var client = ClientWithCatalog(
            new CatalogProduct("PART-1", "Brake Pad", "desc", "Brakes", "active", new CatalogProductMetafields("ea", 1, 1, false)));

        var products = await client.GetFromJsonAsync<List<CatalogProduct>>("/api/catalog");

        Assert.Single(products!);
        Assert.Equal("PART-1", products![0].Sku);
        Assert.Equal("ea", products[0].Metafields.Unit);
    }

    [Fact]
    public async Task Catalog_detail_returns_404_when_missing()
    {
        var client = ClientWithCatalog();
        var response = await client.GetAsync("/api/catalog/NOPE");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
