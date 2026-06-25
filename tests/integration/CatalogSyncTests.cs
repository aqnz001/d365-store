using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PartsPortal.Mocks.ShopifySim;
using PartsPortal.Sync;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// T5 — runs the real CatalogSyncJob (sample BYOD source -> mapper -> HTTP sink) against
/// shopify-sim hosted in-process, proving the catalog populates, lifecycle drives delisting
/// (discontinued -> archived), and re-runs are idempotent (no duplicates). Handover §8, TDD §5.1.
/// </summary>
public class CatalogSyncTests(WebApplicationFactory<ShopifySimApp> factory) : IClassFixture<WebApplicationFactory<ShopifySimApp>>
{
    // Returns the in-process shopify-sim client for any client name the sink requests.
    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static CatalogSyncJob BuildJob(HttpClient shopifyClient) =>
        new(new SampleByodCatalogSource(),
            new ShopifySimCatalogSink(new SingleClientFactory(shopifyClient)),
            NullLogger<CatalogSyncJob>.Instance);

    [Fact]
    public async Task Sync_populates_catalog_and_delists_discontinued_items()
    {
        var client = factory.CreateClient();

        // Expectations derived from the sample BYOD source (robust to sample changes).
        var byod = await new SampleByodCatalogSource().ReadCatalogAsync();
        var expectedActive = byod.Count(p => string.Equals(p.LifecycleState, "Active", StringComparison.OrdinalIgnoreCase));
        var expectedArchived = byod.Count - expectedActive;
        Assert.True(expectedArchived >= 1, "sample must include a discontinued item to exercise delisting");

        var result = await BuildJob(client).RunAsync();

        Assert.Equal(byod.Count, result.Read);
        Assert.Equal(byod.Count, result.Upserted);
        Assert.Equal(expectedArchived, result.Delisted);

        var (total, active, archived) = await ReadCatalogAsync(client);
        Assert.Equal(byod.Count, total);
        Assert.Equal(expectedActive, active);
        Assert.Equal(expectedArchived, archived);
    }

    [Fact]
    public async Task Re_running_the_sync_is_idempotent()
    {
        var client = factory.CreateClient();
        var byod = await new SampleByodCatalogSource().ReadCatalogAsync();

        await BuildJob(client).RunAsync();
        var (firstTotal, _, _) = await ReadCatalogAsync(client);

        await BuildJob(client).RunAsync();
        var (secondTotal, _, _) = await ReadCatalogAsync(client);

        Assert.Equal(byod.Count, firstTotal);
        Assert.Equal(byod.Count, secondTotal); // upsert keyed by SKU — no duplicates
    }

    private static async Task<(int Total, int Active, int Archived)> ReadCatalogAsync(HttpClient client)
    {
        var doc = await client.GetFromJsonAsync<JsonElement>("admin/products");
        var total = doc.GetProperty("count").GetInt32();
        var active = 0;
        var archived = 0;
        foreach (var product in doc.GetProperty("products").EnumerateArray())
        {
            var status = product.GetProperty("status").GetString();
            if (status == ShopifyProductStatus.Active)
            {
                active++;
            }
            else if (status == ShopifyProductStatus.Archived)
            {
                archived++;
            }

            // Every synced product carries its cart-validation metafields (TDD §5.1).
            Assert.False(string.IsNullOrWhiteSpace(product.GetProperty("metafields").GetProperty("unit").GetString()));
        }

        return (total, active, archived);
    }
}
