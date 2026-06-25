using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PartsPortal.Mocks.PricingCreditSim;
using PartsPortal.Shared.Pricing;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// T7 — resolves pricing + credit against pricing-credit-sim in-process: locked prices and
/// the credit gate decision (OK → Approved, over-limit → RequiresApproval, hold → Blocked).
/// Handover §8, TDD §4.5. Distinct customers/items per test avoid shared-state collisions.
/// </summary>
public class PricingCreditTests(WebApplicationFactory<PricingCreditSimApp> factory) : IClassFixture<WebApplicationFactory<PricingCreditSimApp>>
{
    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private (PricingCreditService Service, HttpClient Client) Build()
    {
        var client = factory.CreateClient();
        return (new PricingCreditService(new PricingCreditClient(new SingleClientFactory(client))), client);
    }

    private static Task SeedAsync(HttpClient client, object body) => client.PostAsJsonAsync("/admin/seed", body);

    [Fact]
    public async Task Resolve_locks_prices_and_approves_ok_credit()
    {
        var (service, client) = Build();
        await SeedAsync(client, new
        {
            prices = new[] { new { itemNumber = "ITEM-P", unitPrice = 10m } },
            credit = new[] { new { customerAccount = "C-OK", status = "OK" } },
        });

        var result = await service.ResolveAsync(new PricingResolveRequest("C-OK", [new PricingResolveLine("ITEM-P", 3m)]));

        var line = Assert.Single(result.Lines);
        Assert.Equal(10m, line.UnitPrice);
        Assert.Equal(30m, line.NetEffectivePrice);
        Assert.Equal(CreditDecision.Approved, result.Decision);
    }

    [Fact]
    public async Task Over_limit_credit_requires_approval()
    {
        var (service, client) = Build();
        await SeedAsync(client, new { credit = new[] { new { customerAccount = "C-OVL", status = "over-limit" } } });

        var result = await service.ResolveAsync(new PricingResolveRequest("C-OVL", [new PricingResolveLine("X", 1m)]));

        Assert.Equal(CreditDecision.RequiresApproval, result.Decision);
    }

    [Fact]
    public async Task Hold_credit_is_blocked()
    {
        var (service, client) = Build();
        await SeedAsync(client, new { credit = new[] { new { customerAccount = "C-HOLD", status = "hold" } } });

        var result = await service.ResolveAsync(new PricingResolveRequest("C-HOLD", [new PricingResolveLine("X", 1m)]));

        Assert.Equal(CreditDecision.Blocked, result.Decision);
    }
}
