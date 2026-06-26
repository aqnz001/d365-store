using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PartsPortal.Mocks.IvsSim;
using PartsPortal.Shared.Ivs;
using PartsPortal.Shared.Observability;
using PartsPortal.Shared.Reservations;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// T12 — the TTL reservation-release job against ivs-sim: stale soft reservations are released
/// (AFR restored), fresh ones are kept, and converted ones are never released (TDD §7.1, §12).
/// </summary>
public class ReservationReleaseTests(WebApplicationFactory<IvsSimApp> factory) : IClassFixture<WebApplicationFactory<IvsSimApp>>
{
    private const string Site = "1";
    private const string Location = "11";
    private static readonly DateTimeOffset Now = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CountingMetrics : IPortalMetrics
    {
        public int ReleasedTotal { get; private set; }

        public void ReserveShortfall() { }
        public void ReservationsReleased(int count) => ReleasedTotal += count;
        public void OrderDeadLettered() { }
        public void CatalogSynced(int upserted) { }
    }

    private (ReservationReleaseService Service, IvsClient Ivs, InMemoryReservationRegistry Registry, HttpClient Client, CountingMetrics Metrics) Build()
    {
        var client = factory.CreateClient();
        var options = Options.Create(new IvsOptions { EnvironmentId = "usmf", DefaultLocation = Location, ReservationTtlSeconds = 900 });
        var ivs = new IvsClient(new SingleClientFactory(client), options);
        var registry = new InMemoryReservationRegistry();
        var metrics = new CountingMetrics();
        return (new ReservationReleaseService(registry, ivs, options, metrics, NullLogger<ReservationReleaseService>.Instance), ivs, registry, client, metrics);
    }

    private static Task SeedAsync(HttpClient client, string product)
        => client.PostAsJsonAsync("/admin/seed", new { items = new[] { new { productId = product, site = Site, location = Location, afr = 5m, atp = 5m } } });

    [Fact]
    public async Task Releases_stale_soft_reservations_and_keeps_fresh_ones()
    {
        var (service, ivs, registry, client, metrics) = Build();

        await SeedAsync(client, "P-STALE");
        var stale = await ivs.ReserveAsync("P-STALE", Site, Location, 2); // AFR 5 -> 3
        registry.Record(stale.ReservationId!, Now.AddHours(-1), "c"); // older than the 900s TTL

        await SeedAsync(client, "P-FRESH");
        var fresh = await ivs.ReserveAsync("P-FRESH", Site, Location, 2); // AFR 5 -> 3
        registry.Record(fresh.ReservationId!, Now, "c");

        var released = await service.ReleaseStaleAsync(Now);

        Assert.Equal(1, released);
        Assert.Equal(1, metrics.ReleasedTotal); // reservation-leak metric emitted
        Assert.Equal(5m, (await ivs.QueryAtpAsync("P-STALE", Site, Location)).Afr); // released → restored
        Assert.Equal(3m, (await ivs.QueryAtpAsync("P-FRESH", Site, Location)).Afr); // kept
    }

    [Fact]
    public async Task Never_releases_converted_reservations()
    {
        var (service, ivs, registry, client, _) = Build();

        await SeedAsync(client, "P-CONV");
        var reservation = await ivs.ReserveAsync("P-CONV", Site, Location, 2); // AFR 5 -> 3
        registry.Record(reservation.ReservationId!, Now.AddHours(-1), "c");
        registry.MarkConverted(reservation.ReservationId!); // order written back → physical

        var released = await service.ReleaseStaleAsync(Now);

        Assert.Equal(0, released);
        Assert.Equal(3m, (await ivs.QueryAtpAsync("P-CONV", Site, Location)).Afr); // stays consumed
    }
}
