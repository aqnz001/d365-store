using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using PartsPortal.Mocks.IvsSim;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// T3 — exercises ivs-sim in-process: ATP/AFR query, reserve (success + shortfall),
/// release, allocation, and forward-dated ATP (Handover §8, TDD §4.1–4.3, §6.4–6.5).
/// Each test uses distinct product ids so the shared in-memory state does not collide.
/// </summary>
public class IvsSimTests(WebApplicationFactory<IvsSimApp> factory) : IClassFixture<WebApplicationFactory<IvsSimApp>>
{
    private const string Env = "usmf";

    private async Task SeedAsync(HttpClient client, object item)
        => (await client.PostJsonAsync("/admin/seed", new { items = new[] { item } }))
            .EnsureSuccessStatusCode();

    [Fact]
    public async Task IndexQuery_returns_seeded_atp_and_afr()
    {
        var client = factory.CreateClient();
        await SeedAsync(client, new { productId = "P-IDX", site = "1", location = "11", afr = 10m, atp = 8m });

        var resp = await client.PostJsonAsync(
            $"/api/environment/{Env}/onhand/indexquery?QueryATP=true",
            new { products = new[] { new { productId = "P-IDX", site = "1", location = "11" } } });

        resp.EnsureSuccessStatusCode();
        var line = (await resp.ReadJsonAsync()).GetProperty("results")[0];
        Assert.Equal(10m, line.GetProperty("afr").GetDecimal());
        Assert.Equal(8m, line.GetProperty("atp").GetDecimal());
    }

    [Fact]
    public async Task Reserve_succeeds_returns_id_and_decrements_afr()
    {
        var client = factory.CreateClient();
        await SeedAsync(client, new { productId = "P-RES", site = "1", location = "11", afr = 5m });

        var reserve = await client.PostJsonAsync(
            $"/api/environment/{Env}/onhand/reserve",
            new { productId = "P-RES", site = "1", location = "11", quantity = 2m, ifCheckAvailForReserv = true });

        reserve.EnsureSuccessStatusCode();
        var body = await reserve.ReadJsonAsync();
        Assert.Equal("Reserved", body.GetProperty("status").GetString());
        Assert.StartsWith("RSV-", body.GetProperty("reservationId").GetString());

        var query = await client.PostJsonAsync(
            $"/api/environment/{Env}/onhand/indexquery?QueryATP=true",
            new { products = new[] { new { productId = "P-RES", site = "1", location = "11" } } });
        var afr = (await query.ReadJsonAsync()).GetProperty("results")[0].GetProperty("afr").GetDecimal();
        Assert.Equal(3m, afr);
    }

    [Fact]
    public async Task Reserve_returns_409_shortfall_when_afr_insufficient()
    {
        var client = factory.CreateClient();
        await SeedAsync(client, new { productId = "P-SF", site = "1", location = "11", afr = 1m });

        var resp = await client.PostJsonAsync(
            $"/api/environment/{Env}/onhand/reserve",
            new { productId = "P-SF", site = "1", location = "11", quantity = 5m, ifCheckAvailForReserv = true });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.ReadJsonAsync();
        Assert.Equal("Shortfall", body.GetProperty("status").GetString());
        Assert.Equal(1m, body.GetProperty("availableQuantity").GetDecimal());
    }

    [Fact]
    public async Task Reserve_returns_409_when_shortfall_injected_by_header()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-sim-shortfall", "true");
        await SeedAsync(client, new { productId = "P-HDR", site = "1", location = "11", afr = 100m });

        var resp = await client.PostJsonAsync(
            $"/api/environment/{Env}/onhand/reserve",
            new { productId = "P-HDR", site = "1", location = "11", quantity = 1m, ifCheckAvailForReserv = true });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Release_restores_afr()
    {
        var client = factory.CreateClient();
        await SeedAsync(client, new { productId = "P-REL", site = "1", location = "11", afr = 4m });

        var reserve = await client.PostJsonAsync(
            $"/api/environment/{Env}/onhand/reserve",
            new { productId = "P-REL", site = "1", location = "11", quantity = 4m, ifCheckAvailForReserv = true });
        var reservationId = (await reserve.ReadJsonAsync()).GetProperty("reservationId").GetString();

        var release = await client.PostJsonAsync(
            $"/api/environment/{Env}/onhand/release", new { reservationId });
        release.EnsureSuccessStatusCode();
        Assert.Equal("Released", (await release.ReadJsonAsync()).GetProperty("status").GetString());

        var query = await client.PostJsonAsync(
            $"/api/environment/{Env}/onhand/indexquery?QueryATP=true",
            new { products = new[] { new { productId = "P-REL", site = "1", location = "11" } } });
        var afr = (await query.ReadJsonAsync()).GetProperty("results")[0].GetProperty("afr").GetDecimal();
        Assert.Equal(4m, afr);
    }

    [Fact]
    public async Task Allocation_reduces_afr()
    {
        var client = factory.CreateClient();
        await SeedAsync(client, new { productId = "P-ALLOC", site = "1", location = "11", afr = 10m });

        var alloc = await client.PostJsonAsync(
            $"/api/environment/{Env}/allocation",
            new { productId = "P-ALLOC", site = "1", location = "11", quantity = 3m });
        alloc.EnsureSuccessStatusCode();
        Assert.Equal(7m, (await alloc.ReadJsonAsync()).GetProperty("remainingAfr").GetDecimal());
    }

    [Fact]
    public async Task Forward_dated_atp_includes_scheduled_inbound_on_or_after_inbound_date()
    {
        var client = factory.CreateClient();
        await SeedAsync(client, new
        {
            productId = "P-FWD",
            site = "1",
            location = "11",
            afr = 0m,
            atp = 0m,
            inboundAtp = 20m,
            inboundDate = "2026-07-01",
        });

        // Present-time query: no inbound counted.
        var now = await client.PostJsonAsync(
            $"/api/environment/{Env}/onhand/indexquery?QueryATP=true",
            new { products = new[] { new { productId = "P-FWD", site = "1", location = "11" } } });
        Assert.Equal(0m, (await now.ReadJsonAsync()).GetProperty("results")[0].GetProperty("atp").GetDecimal());

        // Forward-dated query on/after the inbound date: inbound counts toward ATP, not AFR.
        var future = await client.PostJsonAsync(
            $"/api/environment/{Env}/onhand/indexquery?QueryATP=true",
            new { products = new[] { new { productId = "P-FWD", site = "1", location = "11" } }, date = "2026-07-15" });
        var line = (await future.ReadJsonAsync()).GetProperty("results")[0];
        Assert.Equal(20m, line.GetProperty("atp").GetDecimal());
        Assert.Equal(0m, line.GetProperty("afr").GetDecimal());
    }
}
