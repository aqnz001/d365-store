using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PartsPortal.Mocks.IvsSim;
using PartsPortal.Mocks.ODataSim;
using PartsPortal.Shared.Contracts.Messages;
using PartsPortal.Shared.Idempotency;
using PartsPortal.Shared.Ivs;
using PartsPortal.Shared.Observability;
using PartsPortal.Shared.Reservations;
using PartsPortal.Shared.Writeback;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// T9 — order writeback against odata-sim + ivs-sim in-process: happy path (header→lines,
/// reservation converts/persists), duplicate de-dup, permanent failure (compensate = release
/// reservation), and transient failure (propagates for retry). Handover §8, TDD §6.2, §8.
/// </summary>
public class OrderWritebackTests : IClassFixture<WebApplicationFactory<ODataSimApp>>, IClassFixture<WebApplicationFactory<IvsSimApp>>
{
    private const string Site = "1";
    private const string Location = "11";

    private readonly WebApplicationFactory<ODataSimApp> _odataFactory;
    private readonly WebApplicationFactory<IvsSimApp> _ivsFactory;

    public OrderWritebackTests(WebApplicationFactory<ODataSimApp> odata, WebApplicationFactory<IvsSimApp> ivs)
    {
        _odataFactory = odata;
        _ivsFactory = ivs;
    }

    private sealed class NamedClientFactory(Dictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients[name];
    }

    private (OrderWritebackService Service, IIvsClient Ivs, HttpClient Odata, HttpClient IvsHttp) Build(HttpClient? odataOverride = null)
    {
        var odata = odataOverride ?? _odataFactory.CreateClient();
        var ivsHttp = _ivsFactory.CreateClient();
        var factory = new NamedClientFactory(new Dictionary<string, HttpClient> { ["odata"] = odata, ["ivs"] = ivsHttp });
        var ivsClient = new IvsClient(factory, Options.Create(new IvsOptions { EnvironmentId = "usmf", DefaultLocation = Location }));
        var service = new OrderWritebackService(new InMemoryIdempotencyStore(), new ODataOrderClient(factory), ivsClient, new InMemoryReservationRegistry(), NoOpPortalMetrics.Instance, NullLogger<OrderWritebackService>.Instance);
        return (service, ivsClient, odata, ivsHttp);
    }

    private static Task SeedOdataAsync(HttpClient odata, string[]? items = null, string[]? customers = null)
        => odata.PostAsJsonAsync("/admin/seed", new { items = items ?? [], customers = customers ?? [] });

    private static Task SeedIvsAsync(HttpClient ivs, string product, decimal afr)
        => ivs.PostAsJsonAsync("/admin/seed", new { items = new[] { new { productId = product, site = Site, location = Location, afr, atp = afr } } });

    private static OrderInboundMessage Message(string idempotencyKey, string customer, string item, double quantity, string reservationRef)
    {
        var message = new OrderInboundMessage
        {
            IdempotencyKey = idempotencyKey,
            CorrelationId = "corr",
            SessionId = customer,
            CustomerAccount = customer,
            Currency = "GBP",
            PlacedAtUtc = new DateTimeOffset(2026, 6, 26, 9, 0, 0, TimeSpan.Zero),
        };
        message.Lines.Add(new OrderLine
        {
            ItemNumber = item,
            Quantity = quantity,
            Unit = "ea",
            Site = Site,
            Backorder = false,
            ReservationReference = reservationRef,
        });
        return message;
    }

    [Fact]
    public async Task Happy_path_creates_order_and_keeps_reservation()
    {
        var (service, ivs, odata, ivsHttp) = Build();
        await SeedOdataAsync(odata, items: ["ITEM-1"], customers: ["C-1"]);
        await SeedIvsAsync(ivsHttp, "ITEM-1", afr: 5);
        var reservation = await ivs.ReserveAsync("ITEM-1", Site, Location, 2); // AFR 5 -> 3

        var result = await service.ProcessAsync(Message("idem-1", "C-1", "ITEM-1", 2, reservation.ReservationId!));

        Assert.Equal(WritebackStatus.Created, result.Status);
        Assert.StartsWith("SO-", result.SalesOrderNumber);
        // Reservation converted (left in place), not released — AFR stays decremented.
        Assert.Equal(3m, (await ivs.QueryAtpAsync("ITEM-1", Site, Location)).Afr);
    }

    [Fact]
    public async Task Duplicate_message_returns_existing_order_without_recreating()
    {
        var (service, _, odata, _) = Build();
        await SeedOdataAsync(odata, items: ["ITEM-D"], customers: ["C-D"]);
        var message = Message("idem-dup", "C-D", "ITEM-D", 1, "RSV-x");

        var first = await service.ProcessAsync(message);
        var second = await service.ProcessAsync(message);

        Assert.Equal(WritebackStatus.Created, first.Status);
        Assert.Equal(WritebackStatus.Duplicate, second.Status);
        Assert.Equal(first.SalesOrderNumber, second.SalesOrderNumber); // same order; no re-create
    }

    [Fact]
    public async Task Permanent_failure_compensates_by_releasing_the_reservation()
    {
        var (service, ivs, odata, ivsHttp) = Build();
        await SeedOdataAsync(odata, customers: ["C-2"]); // item ITEM-BAD intentionally NOT seeded
        await SeedIvsAsync(ivsHttp, "ITEM-2", afr: 5);
        var reservation = await ivs.ReserveAsync("ITEM-2", Site, Location, 2); // AFR 5 -> 3

        var result = await service.ProcessAsync(Message("idem-2", "C-2", "ITEM-BAD", 1, reservation.ReservationId!));

        Assert.Equal(WritebackStatus.PermanentFailure, result.Status);
        // Compensation released the reservation — AFR restored.
        Assert.Equal(5m, (await ivs.QueryAtpAsync("ITEM-2", Site, Location)).Afr);
    }

    [Fact]
    public async Task Transient_failure_propagates_for_retry()
    {
        var odataWithFault = _odataFactory.CreateClient();
        odataWithFault.DefaultRequestHeaders.Add("x-sim-fail", "transient");
        var (service, _, _, _) = Build(odataOverride: odataWithFault);

        await Assert.ThrowsAsync<TransientWritebackException>(
            () => service.ProcessAsync(Message("idem-3", "C-3", "ITEM-3", 1, "RSV-y")));
    }
}
