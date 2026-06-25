using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using PartsPortal.Mocks.IvsSim;
using PartsPortal.Shared.Availability;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Ivs;
using PartsPortal.Shared.Mapping;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// T6 — exercises the cart availability flow against ivs-sim in-process: validate bands +
/// decisions, reserve (success decrements AFR; all-or-nothing shortfall restores it), and
/// release (TDD §6.1, §7.2). Distinct product ids per test avoid shared-state collisions.
/// </summary>
public class AvailabilityTests(WebApplicationFactory<IvsSimApp> factory) : IClassFixture<WebApplicationFactory<IvsSimApp>>
{
    private const string Site = "1";
    private const string Location = "11";

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private (CartAvailabilityService Service, IvsClient Ivs, HttpClient Client) Build()
    {
        var client = factory.CreateClient();
        var ivsOptions = Options.Create(new IvsOptions { EnvironmentId = "usmf", DefaultLocation = Location, ReservationTtlSeconds = 900 });
        var ivs = new IvsClient(new SingleClientFactory(client), ivsOptions);
        var calculator = new AvailabilityBandCalculator(new AvailabilityOptions { DefaultBuffer = 0m, LowStockThreshold = 5m });
        return (new CartAvailabilityService(ivs, calculator, ivsOptions), ivs, client);
    }

    private static async Task SeedAsync(HttpClient client, string productId, decimal afr, decimal atp)
        => (await client.PostAsJsonAsync("/admin/seed", new
        {
            items = new[] { new { productId, site = Site, location = Location, afr, atp } },
        })).EnsureSuccessStatusCode();

    private static CartValidateRequest ValidateRequest(string itemNumber, double quantity)
    {
        var request = new CartValidateRequest { Customer = new CustomerRef { CustomerAccount = "C-1" } };
        request.Lines.Add(new CartLineInput { ItemNumber = itemNumber, Quantity = quantity, Site = Site });
        return request;
    }

    private static ReserveRequest ReserveRequest(params (string Item, double Qty)[] lines)
    {
        var request = new ReserveRequest { Customer = new CustomerRef { CustomerAccount = "C-1" } };
        foreach (var (item, qty) in lines)
        {
            request.Lines.Add(new CartLineInput { ItemNumber = item, Quantity = qty, Site = Site });
        }

        return request;
    }

    [Theory]
    [InlineData(20, 2, AvailabilityBand.InStock, LineDecision.Allow)]
    [InlineData(3, 2, AvailabilityBand.LowStock, LineDecision.Allow)]
    [InlineData(3, 10, AvailabilityBand.LowStock, LineDecision.ReduceQuantity)]
    [InlineData(0, 1, AvailabilityBand.Unavailable, LineDecision.Block)]
    public async Task Validate_returns_expected_band_and_decision(int atp, int quantity, AvailabilityBand band, LineDecision decision)
    {
        var (service, _, client) = Build();
        var item = $"P-VAL-{atp}-{quantity}";
        await SeedAsync(client, item, afr: atp, atp: atp);

        var response = await service.ValidateAsync(ValidateRequest(item, quantity), "corr");

        var line = Assert.Single(response.Lines);
        Assert.Equal(band, line.Band);
        Assert.Equal(decision, line.Decision);
    }

    [Fact]
    public async Task Reserve_succeeds_returns_ids_and_decrements_afr()
    {
        var (service, ivs, client) = Build();
        await SeedAsync(client, "P-RES", afr: 5, atp: 5);

        var (reserved, response) = await service.ReserveAsync(ReserveRequest(("P-RES", 2)), "corr");

        Assert.True(reserved);
        Assert.Single(response.ReservationIds);
        Assert.Equal(900, response.TtlSeconds);
        Assert.Equal(3m, (await ivs.QueryAtpAsync("P-RES", Site, Location)).Afr);
    }

    [Fact]
    public async Task Reserve_is_all_or_nothing_and_restores_afr_on_shortfall()
    {
        var (service, ivs, client) = Build();
        await SeedAsync(client, "P-OK", afr: 5, atp: 5);
        await SeedAsync(client, "P-SHORT", afr: 1, atp: 1);

        var (reserved, response) = await service.ReserveAsync(ReserveRequest(("P-OK", 2), ("P-SHORT", 5)), "corr");

        Assert.False(reserved);
        Assert.Empty(response.ReservationIds); // no partial reservations held
        // The line that succeeded was rolled back — its AFR is fully restored.
        Assert.Equal(5m, (await ivs.QueryAtpAsync("P-OK", Site, Location)).Afr);
        var shortLine = Assert.Single(response.Lines, l => l.ItemNumber == "P-SHORT");
        Assert.False(shortLine.Reserved);
        Assert.Equal(4, shortLine.Shortfall);
    }

    [Fact]
    public async Task Release_frees_afr()
    {
        var (service, ivs, client) = Build();
        await SeedAsync(client, "P-REL", afr: 4, atp: 4);

        var (_, reserveResponse) = await service.ReserveAsync(ReserveRequest(("P-REL", 4)), "corr");
        Assert.Equal(0m, (await ivs.QueryAtpAsync("P-REL", Site, Location)).Afr);

        var releaseRequest = new ReleaseRequest();
        foreach (var id in reserveResponse.ReservationIds)
        {
            releaseRequest.ReservationIds.Add(id);
        }

        await service.ReleaseAsync(releaseRequest);

        Assert.Equal(4m, (await ivs.QueryAtpAsync("P-REL", Site, Location)).Afr);
    }
}
