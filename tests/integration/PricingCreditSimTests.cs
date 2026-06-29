using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PartsPortal.Mocks.PricingCreditSim;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// T3 — exercises pricing-credit-sim in-process: deterministic effective pricing and the
/// credit status precedence (header &gt; per-customer seed &gt; config default), covering the
/// over-limit / hold negative paths (Handover §8, TDD §4.5).
/// </summary>
public class PricingCreditSimTests(WebApplicationFactory<PricingCreditSimApp> factory)
    : IClassFixture<WebApplicationFactory<PricingCreditSimApp>>
{
    private const string ResolveUrl = "/api/services/PortalPricing/resolve";

    [Fact]
    public async Task Resolve_returns_seeded_price_and_net_effective()
    {
        var client = factory.CreateClient();
        await client.PostJsonAsync("/admin/seed", new { prices = new[] { new { itemNumber = "ITEM-P", unitPrice = 10m } } });

        var resp = await client.PostJsonAsync(
            ResolveUrl,
            new { customerAccount = "C-P", lines = new[] { new { itemNumber = "ITEM-P", quantity = 3m } } });

        resp.EnsureSuccessStatusCode();
        var line = (await resp.ReadJsonAsync()).GetProperty("lines")[0];
        Assert.Equal(10m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(30m, line.GetProperty("netEffectivePrice").GetDecimal());
    }

    [Fact]
    public async Task Resolve_returns_finops_owned_tax_per_line()
    {
        var client = factory.CreateClient();
        await client.PostJsonAsync("/admin/seed", new { prices = new[] { new { itemNumber = "ITEM-T", unitPrice = 10m } } });

        var resp = await client.PostJsonAsync(
            ResolveUrl,
            new { customerAccount = "C-T", lines = new[] { new { itemNumber = "ITEM-T", quantity = 3m } } });

        resp.EnsureSuccessStatusCode();
        var line = (await resp.ReadJsonAsync()).GetProperty("lines")[0];
        // Default Tax:Rate = 0.20 → 30 net × 20% = 6.00 tax (the portal surfaces this, never computes it).
        Assert.Equal(0.20m, line.GetProperty("taxRate").GetDecimal());
        Assert.Equal(6.00m, line.GetProperty("taxAmount").GetDecimal());
    }

    [Fact]
    public async Task Resolve_returns_finops_owned_credit_limit_and_available()
    {
        var client = factory.CreateClient();
        await client.PostJsonAsync("/admin/seed",
            new { credit = new[] { new { customerAccount = "C-CR", status = "OK", creditLimit = 8000m, availableCredit = 1500m } } });

        var resp = await client.PostJsonAsync(
            ResolveUrl,
            new { customerAccount = "C-CR", lines = new[] { new { itemNumber = "X", quantity = 1m } } });

        resp.EnsureSuccessStatusCode();
        var body = await resp.ReadJsonAsync();
        Assert.Equal(8000m, body.GetProperty("creditLimit").GetProperty("amount").GetDecimal());
        Assert.Equal(1500m, body.GetProperty("availableCredit").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Unseeded_item_resolves_to_zero()
    {
        var client = factory.CreateClient();

        var resp = await client.PostJsonAsync(
            ResolveUrl,
            new { customerAccount = "C-Z", lines = new[] { new { itemNumber = "ITEM-NONE", quantity = 5m } } });

        resp.EnsureSuccessStatusCode();
        var line = (await resp.ReadJsonAsync()).GetProperty("lines")[0];
        Assert.Equal(0m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(0m, line.GetProperty("netEffectivePrice").GetDecimal());
    }

    [Fact]
    public async Task Credit_hold_via_request_header()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-sim-credit", "hold");

        var resp = await client.PostJsonAsync(
            ResolveUrl,
            new { customerAccount = "C-H", lines = new[] { new { itemNumber = "X", quantity = 1m } } });

        resp.EnsureSuccessStatusCode();
        Assert.Equal("hold", (await resp.ReadJsonAsync()).GetProperty("creditStatus").GetString());
    }

    [Fact]
    public async Task Credit_over_limit_via_seeded_customer()
    {
        var client = factory.CreateClient();
        await client.PostJsonAsync("/admin/seed", new { credit = new[] { new { customerAccount = "C-OVL", status = "over-limit" } } });

        var resp = await client.PostJsonAsync(
            ResolveUrl,
            new { customerAccount = "C-OVL", lines = new[] { new { itemNumber = "X", quantity = 1m } } });

        resp.EnsureSuccessStatusCode();
        Assert.Equal("over-limit", (await resp.ReadJsonAsync()).GetProperty("creditStatus").GetString());
    }

    [Fact]
    public async Task Default_credit_status_comes_from_configuration()
    {
        // Lowest precedence: no header, customer not seeded -> config default applies.
        var configured = factory.WithWebHostBuilder(b => b.UseSetting("Credit:Status", "over-limit"));
        var client = configured.CreateClient();

        var resp = await client.PostJsonAsync(
            ResolveUrl,
            new { customerAccount = "C-DEFAULT", lines = new[] { new { itemNumber = "X", quantity = 1m } } });

        resp.EnsureSuccessStatusCode();
        Assert.Equal("over-limit", (await resp.ReadJsonAsync()).GetProperty("creditStatus").GetString());
    }
}
