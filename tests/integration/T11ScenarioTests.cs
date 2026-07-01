using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PartsPortal.Mocks.IvsSim;
using PartsPortal.Mocks.ODataSim;
using PartsPortal.Mocks.PricingCreditSim;
using PartsPortal.Shared.Availability;
using PartsPortal.Shared.Contracts.Messages;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Idempotency;
using PartsPortal.Shared.Ivs;
using PartsPortal.Shared.Mapping;
using PartsPortal.Shared.Notifications;
using PartsPortal.Shared.Observability;
using PartsPortal.Shared.Pricing;
using PartsPortal.Shared.Reservations;
using PartsPortal.Shared.Status;
using PartsPortal.Shared.Writeback;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// T11 — the business-scenario matrix (Handover §T11, TDD §6/§7/§9), each proven end-to-end
/// against the Phase-1 mocks. Scenarios covered here: backorder, made-to-order, discontinued,
/// advance/block (allocation), multi-warehouse, partial fulfilment, returns, cancellation
/// (release), recurring (re-check + re-resolve), price integrity (locked-price tolerance), credit
/// hold, and kits (modeled as exploded component lines, reserved all-or-nothing per DR-009).
///
/// Min-qty / order-multiple / UoM validation is exercised at the storefront BFF
/// (<see cref="BffCartCheckoutTests"/>); the reservation state machine (TDD §7.1) by
/// <see cref="AvailabilityTests"/> and <see cref="ReservationReleaseTests"/>.
/// </summary>
public class T11ScenarioTests :
    IClassFixture<WebApplicationFactory<IvsSimApp>>,
    IClassFixture<WebApplicationFactory<ODataSimApp>>,
    IClassFixture<WebApplicationFactory<PricingCreditSimApp>>
{
    private const string Site = "1";
    private const string Location = "11";

    private readonly WebApplicationFactory<IvsSimApp> _ivsFactory;
    private readonly WebApplicationFactory<ODataSimApp> _odataFactory;
    private readonly WebApplicationFactory<PricingCreditSimApp> _pricingFactory;

    public T11ScenarioTests(
        WebApplicationFactory<IvsSimApp> ivs,
        WebApplicationFactory<ODataSimApp> odata,
        WebApplicationFactory<PricingCreditSimApp> pricing)
    {
        _ivsFactory = ivs;
        _odataFactory = odata;
        _pricingFactory = pricing;
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class NamedClientFactory(Dictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients[name];
    }

    private static IvsOptions IvsOpts() =>
        new() { EnvironmentId = "usmf", DefaultLocation = Location, ReservationTtlSeconds = 900 };

    private (CartAvailabilityService Service, IvsClient Ivs, HttpClient Http) BuildAvailability()
    {
        var http = _ivsFactory.CreateClient();
        var options = Options.Create(IvsOpts());
        var ivs = new IvsClient(new SingleClientFactory(http), options);
        var calculator = new AvailabilityBandCalculator(new AvailabilityOptions { DefaultBuffer = 0m, LowStockThreshold = 5m });
        var service = new CartAvailabilityService(ivs, calculator, options, new InMemoryReservationRegistry(), NoOpPortalMetrics.Instance);
        return (service, ivs, http);
    }

    private static Task SeedIvsAsync(HttpClient http, string product, decimal afr, decimal? atp = null, string location = Location)
        => http.PostAsJsonAsync("/admin/seed", new
        {
            items = new[] { new { productId = product, site = Site, location, afr, atp = atp ?? afr } },
        });

    private static CartValidateRequest ValidateRequest(
        string item, double qty, bool backorderable = false, bool madeToOrder = false, bool discontinued = false)
    {
        var request = new CartValidateRequest { Customer = new CustomerRef { CustomerAccount = "C-1" } };
        request.Lines.Add(new CartLineInput
        {
            ItemNumber = item,
            Quantity = qty,
            Site = Site,
            Backorderable = backorderable,
            MadeToOrder = madeToOrder,
            Discontinued = discontinued,
        });
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

    // ---- Availability bands driven by catalog attributes (TDD §7.2) --------------------

    [Fact]
    public async Task Backorder_zero_stock_but_backorderable_is_orderable_as_backorder()
    {
        var (service, _, http) = BuildAvailability();
        await SeedIvsAsync(http, "P-BACK", afr: 0, atp: 0);

        var line = Assert.Single((await service.ValidateAsync(ValidateRequest("P-BACK", 3, backorderable: true), "corr")).Lines);

        Assert.Equal(AvailabilityBand.Backorder, line.Band);
        Assert.Equal(LineDecision.AllowBackorder, line.Decision);
    }

    [Fact]
    public async Task Made_to_order_item_is_orderable_regardless_of_stock()
    {
        var (service, _, http) = BuildAvailability();
        await SeedIvsAsync(http, "P-MTO", afr: 0, atp: 0);

        var line = Assert.Single((await service.ValidateAsync(ValidateRequest("P-MTO", 50, madeToOrder: true), "corr")).Lines);

        Assert.Equal(AvailabilityBand.MadeToOrder, line.Band);
        Assert.Equal(LineDecision.Allow, line.Decision);
    }

    [Fact]
    public async Task Discontinued_item_is_unavailable_even_with_stock_on_hand()
    {
        var (service, _, http) = BuildAvailability();
        await SeedIvsAsync(http, "P-DISC", afr: 100, atp: 100);

        var line = Assert.Single((await service.ValidateAsync(ValidateRequest("P-DISC", 1, discontinued: true), "corr")).Lines);

        Assert.Equal(AvailabilityBand.Unavailable, line.Band);
        Assert.Equal(LineDecision.Block, line.Decision);
    }

    // ---- Advance / block via IVS allocation (TDD §6.4) ---------------------------------

    [Fact]
    public async Task Allocation_ring_fences_stock_and_reduces_what_others_can_reserve()
    {
        var (service, ivs, http) = BuildAvailability();
        await SeedIvsAsync(http, "P-ALLOC", afr: 10, atp: 10);

        // Advance order ring-fences 8 units → only 2 remain reservable for everyone else.
        var allocation = await http.PostAsJsonAsync("/api/environment/usmf/allocation",
            new { productId = "P-ALLOC", site = Site, location = Location, quantity = 8m });
        allocation.EnsureSuccessStatusCode();
        Assert.Equal(2m, (await ivs.QueryAtpAsync("P-ALLOC", Site, Location)).Afr);

        // Reserving 5 now falls short (all-or-nothing); reserving the remaining 2 succeeds.
        var (over, _) = await service.ReserveAsync(ReserveRequest(("P-ALLOC", 5)), "corr");
        Assert.False(over);

        var (within, response) = await service.ReserveAsync(ReserveRequest(("P-ALLOC", 2)), "corr");
        Assert.True(within);
        Assert.Single(response.ReservationIds);
    }

    // ---- Multi-warehouse: branch-specific availability (Open Decision #7) ---------------

    [Fact]
    public async Task Multi_warehouse_availability_is_branch_specific_and_reserves_are_isolated()
    {
        const string branchA = "11";
        const string branchB = "22";
        var http = _ivsFactory.CreateClient();
        var ivs = new IvsClient(new SingleClientFactory(http), Options.Create(IvsOpts()));
        await SeedIvsAsync(http, "P-WH", afr: 20, atp: 20, location: branchA);
        await SeedIvsAsync(http, "P-WH", afr: 0, atp: 0, location: branchB);

        Assert.Equal(20m, (await ivs.QueryAtpAsync("P-WH", Site, branchA)).Atp);
        Assert.Equal(0m, (await ivs.QueryAtpAsync("P-WH", Site, branchB)).Atp);

        // Reserving from branch A must not touch branch B's pool.
        var reserved = await ivs.ReserveAsync("P-WH", Site, branchA, 5);
        Assert.True(reserved.Reserved);
        Assert.Equal(15m, (await ivs.QueryAtpAsync("P-WH", Site, branchA)).Afr);
        Assert.Equal(0m, (await ivs.QueryAtpAsync("P-WH", Site, branchB)).Afr);
    }

    // ---- Kit: a bundle reserves all-or-nothing across its component lines (DR-009) ------

    [Fact]
    public async Task Kit_reserves_all_components_or_none()
    {
        var (service, ivs, http) = BuildAvailability();
        await SeedIvsAsync(http, "KIT-BODY", afr: 5, atp: 5);   // component A
        await SeedIvsAsync(http, "KIT-SEAL", afr: 1, atp: 1);   // component B (scarce)

        // Kit needs 2× body + 5× seal → seal is short, so the whole kit fails and A is restored.
        var (assembled, response) = await service.ReserveAsync(ReserveRequest(("KIT-BODY", 2), ("KIT-SEAL", 5)), "corr");
        Assert.False(assembled);
        Assert.Empty(response.ReservationIds);
        Assert.Equal(5m, (await ivs.QueryAtpAsync("KIT-BODY", Site, Location)).Afr);

        // With enough of every component the kit reserves in full.
        var (ok, okResponse) = await service.ReserveAsync(ReserveRequest(("KIT-BODY", 2), ("KIT-SEAL", 1)), "corr");
        Assert.True(ok);
        Assert.Equal(2, okResponse.ReservationIds.Count);
    }

    // ---- Cancellation: releasing the soft reservation restores AFR (TDD §7.1) -----------

    [Fact]
    public async Task Cancellation_at_the_gate_releases_the_reservation()
    {
        var (service, ivs, http) = BuildAvailability();
        await SeedIvsAsync(http, "P-CANCEL", afr: 4, atp: 4);

        var (_, reserve) = await service.ReserveAsync(ReserveRequest(("P-CANCEL", 4)), "corr");
        Assert.Equal(0m, (await ivs.QueryAtpAsync("P-CANCEL", Site, Location)).Afr);

        var release = new ReleaseRequest();
        foreach (var id in reserve.ReservationIds)
        {
            release.ReservationIds.Add(id);
        }

        await service.ReleaseAsync(release);
        Assert.Equal(4m, (await ivs.QueryAtpAsync("P-CANCEL", Site, Location)).Afr);
    }

    // ---- Status sync: partial fulfilment, returns, cancellation (TDD §6.3) --------------

    private static StatusSyncService BuildStatusSync(IOrderStatusStore store) =>
        new(store,
            new ConfigNotificationContacts(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()),
            new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance),
            NullLogger<StatusSyncService>.Instance);

    [Fact]
    public async Task Partial_fulfilment_then_completion_and_return_walks_the_status_mirror()
    {
        var store = new InMemoryOrderStatusStore();
        var sync = BuildStatusSync(store);

        await sync.ApplyAsync(ShipEvent("SO-T11", "TRK-1", "ITEM-1", quantity: 2, remainingBackorder: 3));
        var partial = store.Get("SO-T11")!;
        Assert.Equal(StorefrontOrderStatus.PartiallyShipped, partial.Status);
        Assert.Equal(3m, partial.RemainingBackorder);

        await sync.ApplyAsync(ShipEvent("SO-T11", "TRK-2", "ITEM-1", quantity: 3, remainingBackorder: 0));
        var complete = store.Get("SO-T11")!;
        Assert.Equal(StorefrontOrderStatus.Shipped, complete.Status);
        Assert.Equal(2, complete.Fulfilments.Count); // one order, two shipments

        // A customer return against the shipped order moves it to Returned (fulfilments preserved).
        await sync.ApplyAsync(new FulfilmentStatusEvent
        {
            SalesOrderNumber = "SO-T11",
            EventType = FulfilmentStatusEventEventType.Returned,
            CorrelationId = "corr",
            OccurredAtUtc = new DateTimeOffset(2026, 6, 27, 9, 0, 0, TimeSpan.Zero),
        });
        var returned = store.Get("SO-T11")!;
        Assert.Equal(StorefrontOrderStatus.Returned, returned.Status);
        Assert.Equal(2, returned.Fulfilments.Count);
    }

    [Fact]
    public async Task Cancelled_status_event_marks_the_order_cancelled()
    {
        var store = new InMemoryOrderStatusStore();
        var sync = BuildStatusSync(store);

        await sync.ApplyAsync(new FulfilmentStatusEvent
        {
            SalesOrderNumber = "SO-CXL",
            EventType = FulfilmentStatusEventEventType.Cancelled,
            CorrelationId = "corr",
            OccurredAtUtc = new DateTimeOffset(2026, 6, 27, 9, 0, 0, TimeSpan.Zero),
        });

        Assert.Equal(StorefrontOrderStatus.Cancelled, store.Get("SO-CXL")!.Status);
    }

    // ---- Recurring: each run re-checks availability and re-resolves price (TDD §6.6) ----

    [Fact]
    public async Task Recurring_order_re_resolves_price_on_each_run()
    {
        var http = _pricingFactory.CreateClient();
        var pricing = new PricingCreditService(new PricingCreditClient(new SingleClientFactory(http)));

        await http.PostAsJsonAsync("/admin/seed", new
        {
            prices = new[] { new { itemNumber = "ITEM-R", unitPrice = 10m } },
            credit = new[] { new { customerAccount = "C-REC", status = "OK" } },
        });

        var firstRun = await pricing.ResolveAsync(new PricingResolveRequest("C-REC", [new PricingResolveLine("ITEM-R", 1m)]));
        Assert.Equal(10m, Assert.Single(firstRun.Lines).UnitPrice);

        // The trade-agreement price changes between recurrences; the next run must pick it up.
        await http.PostAsJsonAsync("/admin/seed", new { prices = new[] { new { itemNumber = "ITEM-R", unitPrice = 12m } } });

        var secondRun = await pricing.ResolveAsync(new PricingResolveRequest("C-REC", [new PricingResolveLine("ITEM-R", 1m)]));
        Assert.Equal(12m, Assert.Single(secondRun.Lines).UnitPrice);
    }

    // ---- Credit hold blocks the order (TDD §4.5) ---------------------------------------

    [Fact]
    public async Task Credit_hold_blocks_the_order()
    {
        var http = _pricingFactory.CreateClient();
        var pricing = new PricingCreditService(new PricingCreditClient(new SingleClientFactory(http)));
        await http.PostAsJsonAsync("/admin/seed", new { credit = new[] { new { customerAccount = "C-T11-HOLD", status = "hold" } } });

        var result = await pricing.ResolveAsync(new PricingResolveRequest("C-T11-HOLD", [new PricingResolveLine("X", 1m)]));

        Assert.Equal(CreditDecision.Blocked, result.Decision);
    }

    // ---- Price integrity at writeback (TDD §9) -----------------------------------------

    [Fact]
    public async Task Price_within_tolerance_is_honored_and_written_back()
    {
        var (service, _, odata, _) = BuildWriteback(toleranceFraction: 0.05m);
        await SeedOdataAsync(odata, item: "ITEM-PINT", customer: "C-PINT", currentPrice: 10.00m);

        // Locked at 10.20 vs current 10.00 → 2% drift, within 5% → created.
        var result = await service.ProcessAsync(OrderMessage("idem-pint-ok", "C-PINT", "ITEM-PINT", lockedAmount: 10.20m, reservationRef: "RSV-pint"));

        Assert.Equal(WritebackStatus.Created, result.Status);
        Assert.StartsWith("SO-", result.SalesOrderNumber);
    }

    [Fact]
    public async Task Price_beyond_tolerance_routes_to_csr_and_releases_the_reservation()
    {
        var (service, ivs, odata, ivsHttp) = BuildWriteback(toleranceFraction: 0.05m);
        await SeedOdataAsync(odata, item: "ITEM-PBAD", customer: "C-PBAD", currentPrice: 10.00m);
        await SeedIvsAsync(ivsHttp, "ITEM-PBAD", afr: 5);
        var reservation = await ivs.ReserveAsync("ITEM-PBAD", Site, Location, 2); // AFR 5 → 3

        // Locked at 12.00 vs current 10.00 → 20% drift, beyond 5% → CSR review (permanent).
        var result = await service.ProcessAsync(OrderMessage("idem-pint-bad", "C-PBAD", "ITEM-PBAD", lockedAmount: 12.00m, reservationRef: reservation.ReservationId!));

        Assert.Equal(WritebackStatus.PermanentFailure, result.Status);
        // Compensation released the soft reservation so stock is not held for a CSR-blocked order.
        Assert.Equal(5m, (await ivs.QueryAtpAsync("ITEM-PBAD", Site, Location)).Afr);
    }

    // ---- Writeback harness -------------------------------------------------------------

    private (OrderWritebackService Service, IvsClient Ivs, HttpClient Odata, HttpClient IvsHttp) BuildWriteback(decimal toleranceFraction)
    {
        var odata = _odataFactory.CreateClient();
        var ivsHttp = _ivsFactory.CreateClient();
        var factory = new NamedClientFactory(new Dictionary<string, HttpClient> { ["odata"] = odata, ["ivs"] = ivsHttp });
        var ivs = new IvsClient(factory, Options.Create(IvsOpts()));
        var policy = new PriceIntegrityPolicy(new PriceIntegrityOptions { ToleranceFraction = toleranceFraction });
        var service = new OrderWritebackService(
            new InMemoryIdempotencyStore(), new ODataOrderClient(factory), ivs,
            new InMemoryReservationRegistry(), NoOpPortalMetrics.Instance,
            NullLogger<OrderWritebackService>.Instance, policy);
        return (service, ivs, odata, ivsHttp);
    }

    private static Task SeedOdataAsync(HttpClient odata, string item, string customer, decimal currentPrice)
        => odata.PostAsJsonAsync("/admin/seed", new
        {
            items = new[] { item },
            customers = new[] { customer },
            prices = new[] { new { itemNumber = item, price = currentPrice } },
        });

    private static OrderInboundMessage OrderMessage(string idempotencyKey, string customer, string item, decimal lockedAmount, string reservationRef)
    {
        var message = new OrderInboundMessage
        {
            IdempotencyKey = idempotencyKey,
            CorrelationId = "corr",
            SessionId = customer,
            CustomerAccount = customer,
            Currency = "GBP",
            PlacedAtUtc = new DateTimeOffset(2026, 6, 27, 9, 0, 0, TimeSpan.Zero),
        };
        message.Lines.Add(new OrderLine
        {
            ItemNumber = item,
            Quantity = 2,
            Unit = "ea",
            Site = Site,
            Backorder = false,
            ReservationReference = reservationRef,
            LockedPrice = new PartsPortal.Shared.Contracts.Messages.Money { Amount = (double)lockedAmount, Currency = "GBP" },
        });
        return message;
    }

    private static FulfilmentStatusEvent ShipEvent(string salesOrderNumber, string tracking, string item, double quantity, double remainingBackorder)
    {
        var shipment = new Shipment { TrackingNumber = tracking };
        shipment.Lines.Add(new ShipmentLine { ItemNumber = item, Quantity = quantity });
        return new FulfilmentStatusEvent
        {
            SalesOrderNumber = salesOrderNumber,
            EventType = FulfilmentStatusEventEventType.Shipped,
            Shipments = [shipment],
            RemainingBackorder = remainingBackorder,
            CorrelationId = "corr",
            OccurredAtUtc = new DateTimeOffset(2026, 6, 27, 9, 0, 0, TimeSpan.Zero),
        };
    }
}
