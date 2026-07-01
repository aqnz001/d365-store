using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PartsPortal.Bff;
using PartsPortal.Bff.Auth;
using PartsPortal.Bff.Clients;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Pricing;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// #7 B2B governance — the order approval workflow (DR-027): an over-spend-limit on-account order
/// routes to a pending queue instead of submitting; an Approver/Admin approves it (which re-reserves
/// and submits on account) or rejects it. Each test builds a fresh host, so the in-memory member and
/// approval stores are isolated.
/// </summary>
public class BffApprovalTests(WebApplicationFactory<BffApp> factory) : IClassFixture<WebApplicationFactory<BffApp>>
{
    private sealed class FakeCatalogApi(params CatalogProduct[] products) : ICatalogApi
    {
        public Task<IReadOnlyList<CatalogProduct>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CatalogProduct>>(products);
        public Task<CatalogProduct?> GetAsync(string sku, CancellationToken ct = default) => Task.FromResult(products.FirstOrDefault(p => p.Sku == sku));
    }

    private sealed class FakeMiddlewareApi : IMiddlewareApi
    {
        public int SubmitCount { get; private set; }
        public bool OrderSubmitted => SubmitCount > 0;
        public OrderRequest? SubmittedOrder { get; private set; }
        public decimal UnitPrice { get; set; } = 100m;

        // Fault-injection switches for the hardening tests.
        public bool ThrowOnSubmit { get; set; }
        public bool DropPricingLines { get; set; }
        public List<string> Released { get; } = [];

        public Task<CartValidateResponse> ValidateCartAsync(CartValidateRequest r, string c, CancellationToken ct = default) => Task.FromResult(new CartValidateResponse());

        public Task<(bool Reserved, ReserveResponse Response)> ReserveAsync(ReserveRequest r, string c, CancellationToken ct = default)
        {
            var response = new ReserveResponse();
            response.ReservationIds.Add("RSV-1");
            return Task.FromResult((true, response));
        }

        public Task ReleaseAsync(ReleaseRequest r, string c, CancellationToken ct = default)
        {
            Released.AddRange(r.ReservationIds);
            return Task.CompletedTask;
        }

        public Task<CartPricingResult> ResolvePricingAsync(PricingResolveRequest r, string c, CancellationToken ct = default)
        {
            var lines = DropPricingLines
                ? new List<PricedLine>() // simulate an incomplete re-price (e.g. item discontinued)
                : r.Lines.Select(l => new PricedLine(l.ItemNumber, l.Quantity, UnitPrice, UnitPrice * l.Quantity)).ToList();
            return Task.FromResult(new CartPricingResult("ACME", "Approved", CreditDecision.Approved, lines, null));
        }

        public Task<OrderStatusResponse> SubmitOrderAsync(OrderRequest r, string c, CancellationToken ct = default)
        {
            if (ThrowOnSubmit)
            {
                throw new InvalidOperationException("writeback unavailable");
            }

            SubmitCount++;
            SubmittedOrder = r;
            return Task.FromResult(new OrderStatusResponse { OrderId = "ORD-1", SalesOrderNumber = "SO-1", Status = OrderStatus.Queued });
        }

        public Task<OrderStatusResponse?> GetOrderStatusAsync(string id, string c, CancellationToken ct = default) => Task.FromResult<OrderStatusResponse?>(null);
    }

    private WebApplicationFactory<BffApp> BuildApp(FakeMiddlewareApi middleware) =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<ICatalogApi>(_ => new FakeCatalogApi(new CatalogProduct("PART-1", "Brake Pad", "desc", "Brakes", "active", new CatalogProductMetafields("ea", 1, 1, false))));
            s.AddScoped<IMiddlewareApi>(_ => middleware);
        }));

    private static HttpClient As(WebApplicationFactory<BffApp> app, string user)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthenticationHandler.CustomerHeader, "ACME");
        client.DefaultRequestHeaders.Add(DevAuthenticationHandler.UserHeader, user);
        return client;
    }

    // alice is the bootstrap admin; she adds buyers with the given spend limit.
    private static async Task AddBuyer(HttpClient admin, string user, string name, decimal? spendLimit) =>
        (await admin.PostAsJsonAsync("/api/company/members", new { userId = user, name, role = "Buyer", spendLimit })).EnsureSuccessStatusCode();

    // Places an on-account order for qty × UnitPrice and returns the pay result.
    private static async Task<JsonElement> PlaceOnAccount(HttpClient client, decimal qty)
    {
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = qty, site = "1" });
        await client.PostAsJsonAsync("/api/checkout/start", new { });
        var pay = await client.PostAsJsonAsync("/api/checkout/pay", new
        {
            amount = 0m,
            currency = "GBP",
            paymentToken = "ok",
            reservationIds = new[] { "RSV-1" },
            paymentMethod = "OnAccount",
            poNumber = "PO-9",
        });
        pay.EnsureSuccessStatusCode();
        return await pay.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task An_over_limit_on_account_order_routes_to_approval_and_submits_nothing()
    {
        var middleware = new FakeMiddlewareApi { UnitPrice = 100m };
        var app = BuildApp(middleware);
        var alice = As(app, "alice@acme.test");
        await AddBuyer(alice, "bob@acme.test", "Bob", 50m); // limit £50

        var result = await PlaceOnAccount(As(app, "bob@acme.test"), 2m); // 2 × £100 = £200 > £50

        Assert.Equal("PendingApproval", result.GetProperty("status").GetString());
        Assert.False(middleware.OrderSubmitted); // nothing submitted while it waits

        var queue = await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/approvals");
        var pending = Assert.Single(queue!);
        Assert.Equal("Pending", pending.GetProperty("status").GetString());
        Assert.Equal("bob@acme.test", pending.GetProperty("buyerUserId").GetString());
        Assert.Equal(200, pending.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task An_order_within_the_buyers_spend_limit_is_placed_directly()
    {
        var middleware = new FakeMiddlewareApi { UnitPrice = 100m };
        var app = BuildApp(middleware);
        await AddBuyer(As(app, "alice@acme.test"), "bob@acme.test", "Bob", 500m); // limit £500

        var result = await PlaceOnAccount(As(app, "bob@acme.test"), 2m); // £200 ≤ £500

        Assert.Equal("OrderPlaced", result.GetProperty("status").GetString());
        Assert.True(middleware.OrderSubmitted);
    }

    [Fact]
    public async Task An_approver_approves_and_the_order_is_submitted_on_account()
    {
        var middleware = new FakeMiddlewareApi { UnitPrice = 100m };
        var app = BuildApp(middleware);
        var alice = As(app, "alice@acme.test");
        await AddBuyer(alice, "bob@acme.test", "Bob", 50m);
        await PlaceOnAccount(As(app, "bob@acme.test"), 2m); // → pending

        var queue = await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/approvals");
        var id = queue![0].GetProperty("id").GetString();

        var approve = await alice.PostAsync($"/api/company/approvals/{id}/approve", null);
        approve.EnsureSuccessStatusCode();
        var approved = await approve.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SO-1", approved.GetProperty("orderReference").GetString());

        Assert.True(middleware.OrderSubmitted); // approval re-reserves + submits
        Assert.Equal("OnAccount", middleware.SubmittedOrder!.PaymentMethod);
        Assert.Equal("PO-9", middleware.SubmittedOrder!.PurchaseOrderNumber);
        Assert.Equal(new[] { "RSV-1" }, middleware.SubmittedOrder!.ReservationIds);

        // The request is now marked Approved with its order reference.
        var after = await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/approvals");
        Assert.Equal("Approved", after![0].GetProperty("status").GetString());
        Assert.Equal("SO-1", after![0].GetProperty("orderReference").GetString());
    }

    [Fact]
    public async Task A_buyer_cannot_approve_orders()
    {
        var middleware = new FakeMiddlewareApi { UnitPrice = 100m };
        var app = BuildApp(middleware);
        var alice = As(app, "alice@acme.test");
        await AddBuyer(alice, "bob@acme.test", "Bob", 50m);
        await PlaceOnAccount(As(app, "bob@acme.test"), 2m);

        var id = (await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/approvals"))![0].GetProperty("id").GetString();
        var response = await As(app, "bob@acme.test").PostAsync($"/api/company/approvals/{id}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(middleware.OrderSubmitted);
    }

    [Fact]
    public async Task Rejecting_an_order_places_nothing()
    {
        var middleware = new FakeMiddlewareApi { UnitPrice = 100m };
        var app = BuildApp(middleware);
        var alice = As(app, "alice@acme.test");
        await AddBuyer(alice, "bob@acme.test", "Bob", 50m);
        await PlaceOnAccount(As(app, "bob@acme.test"), 2m);

        var id = (await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/approvals"))![0].GetProperty("id").GetString();
        var reject = await alice.PostAsync($"/api/company/approvals/{id}/reject", null);
        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);

        Assert.False(middleware.OrderSubmitted);
        var after = await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/approvals");
        Assert.Equal("Rejected", after![0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Approving_an_already_decided_order_does_not_submit_twice()
    {
        var middleware = new FakeMiddlewareApi { UnitPrice = 100m };
        var app = BuildApp(middleware);
        var alice = As(app, "alice@acme.test");
        await AddBuyer(alice, "bob@acme.test", "Bob", 50m);
        await PlaceOnAccount(As(app, "bob@acme.test"), 2m);
        var id = (await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/approvals"))![0].GetProperty("id").GetString();

        (await alice.PostAsync($"/api/company/approvals/{id}/approve", null)).EnsureSuccessStatusCode();
        // A second approval of the now-Approved request is rejected — the atomic claim prevents a
        // double submit (which would be two idempotency keys → two orders).
        var again = await alice.PostAsync($"/api/company/approvals/{id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(1, middleware.SubmitCount);
    }

    [Fact]
    public async Task A_writeback_failure_re_opens_the_request_and_releases_the_reservation()
    {
        var middleware = new FakeMiddlewareApi { UnitPrice = 100m };
        var app = BuildApp(middleware);
        var alice = As(app, "alice@acme.test");
        await AddBuyer(alice, "bob@acme.test", "Bob", 50m);
        await PlaceOnAccount(As(app, "bob@acme.test"), 2m);
        var id = (await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/approvals"))![0].GetProperty("id").GetString();

        middleware.ThrowOnSubmit = true;
        middleware.Released.Clear(); // isolate the approval path's release from the pending-routing one
        try { await alice.PostAsync($"/api/company/approvals/{id}/approve", null); } catch { /* 500 */ }

        Assert.Equal(0, middleware.SubmitCount);
        Assert.Contains("RSV-1", middleware.Released); // the reservation we took is released, not leaked
        var after = await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/approvals");
        Assert.Equal("Pending", after![0].GetProperty("status").GetString()); // re-opened, can be retried
    }

    [Fact]
    public async Task An_incomplete_reprice_does_not_book_a_zero_price_order()
    {
        var middleware = new FakeMiddlewareApi { UnitPrice = 100m };
        var app = BuildApp(middleware);
        var alice = As(app, "alice@acme.test");
        await AddBuyer(alice, "bob@acme.test", "Bob", 50m);
        await PlaceOnAccount(As(app, "bob@acme.test"), 2m);
        var id = (await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/approvals"))![0].GetProperty("id").GetString();

        middleware.DropPricingLines = true; // the re-price returns no line for the item
        middleware.Released.Clear();
        var response = await alice.PostAsync($"/api/company/approvals/{id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); // refused, not booked at £0
        Assert.Equal(0, middleware.SubmitCount);
        Assert.Contains("RSV-1", middleware.Released);
        var after = await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/approvals");
        Assert.Equal("Pending", after![0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_buyer_only_sees_their_own_pending_orders()
    {
        var middleware = new FakeMiddlewareApi { UnitPrice = 100m };
        var app = BuildApp(middleware);
        var alice = As(app, "alice@acme.test");
        await AddBuyer(alice, "bob@acme.test", "Bob", 50m);
        await AddBuyer(alice, "carol@acme.test", "Carol", 50m);
        await PlaceOnAccount(As(app, "bob@acme.test"), 2m); // only bob places one

        // Carol (a different buyer) must not see bob's pending order.
        var carolSees = await As(app, "carol@acme.test").GetFromJsonAsync<List<JsonElement>>("/api/company/approvals");
        Assert.Empty(carolSees!);

        // Bob sees his own; alice (admin) sees the whole queue.
        var bobSees = await As(app, "bob@acme.test").GetFromJsonAsync<List<JsonElement>>("/api/company/approvals");
        Assert.Single(bobSees!);
        var aliceSees = await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/approvals");
        Assert.Single(aliceSees!);
    }
}
