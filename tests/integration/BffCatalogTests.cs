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
        var response = await client.GetAsync("/api/catalog/item/NOPE");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static CatalogProduct Product(string sku, string title, string type) =>
        new(sku, title, "desc", type, "active", new CatalogProductMetafields("ea", 1, 1, false));

    [Fact]
    public async Task Catalog_search_paginates_server_side()
    {
        var client = ClientWithCatalog(
            Product("BRK-1", "Brake Pad", "Brakes"),
            Product("BRK-2", "Brake Disc", "Brakes"),
            Product("FLT-1", "Oil Filter", "Filtration"),
            Product("FLT-2", "Air Filter", "Filtration"),
            Product("BLT-1", "V-Belt", "Power"));

        var page1 = await client.GetFromJsonAsync<JsonElement>("/api/catalog/search?page=1&pageSize=2");
        Assert.Equal(5, page1.GetProperty("total").GetInt32());
        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());
        // The full category set is returned even though only one page of items is.
        Assert.Equal(3, page1.GetProperty("categories").GetArrayLength());

        var page3 = await client.GetFromJsonAsync<JsonElement>("/api/catalog/search?page=3&pageSize=2");
        Assert.Equal(1, page3.GetProperty("items").GetArrayLength()); // 5 items, page 3 of size 2 → the last one
    }

    [Fact]
    public async Task Catalog_search_filters_by_query_and_category()
    {
        var client = ClientWithCatalog(
            Product("BRK-1", "Brake Pad", "Brakes"),
            Product("FLT-1", "Oil Filter", "Filtration"),
            Product("FLT-2", "Air Filter", "Filtration"));

        var byQuery = await client.GetFromJsonAsync<JsonElement>("/api/catalog/search?q=oil");
        Assert.Equal(1, byQuery.GetProperty("total").GetInt32());
        Assert.Equal("FLT-1", byQuery.GetProperty("items")[0].GetProperty("sku").GetString());

        var byCategory = await client.GetFromJsonAsync<JsonElement>("/api/catalog/search?category=Filtration");
        Assert.Equal(2, byCategory.GetProperty("total").GetInt32());
    }
}
