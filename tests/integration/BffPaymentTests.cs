using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PartsPortal.Bff;
using PartsPortal.Bff.Auth;
using PartsPortal.Bff.Clients;
using PartsPortal.Bff.Notifications;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Pricing;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// S5 — the BFF payment flow: authorize (FakePaymentProvider) then submit the order. A declined
/// payment never submits an order. Catalog + middleware clients stubbed.
/// </summary>
public class BffPaymentTests(WebApplicationFactory<BffApp> factory) : IClassFixture<WebApplicationFactory<BffApp>>
{
    private sealed class FakeCatalogApi(params CatalogProduct[] products) : ICatalogApi
    {
        public Task<IReadOnlyList<CatalogProduct>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CatalogProduct>>(products);
        public Task<CatalogProduct?> GetAsync(string sku, CancellationToken ct = default) => Task.FromResult(products.FirstOrDefault(p => p.Sku == sku));
    }

    private sealed class FakeMiddlewareApi : IMiddlewareApi
    {
        public OrderStatusResponse OrderAck { get; set; } = new() { OrderId = "ORD-1", SalesOrderNumber = "SO-1", Status = OrderStatus.Queued };
        public bool OrderSubmitted { get; private set; }
        public OrderRequest? SubmittedOrder { get; private set; }

        // Unit price per item the (fake) pricing service returns; net = unit × quantity.
        public decimal UnitPrice { get; set; } = 19.95m;

        // Credit decision the (fake) pricing service returns — drives the on-account gate.
        public CreditDecision Decision { get; set; } = CreditDecision.Approved;

        // Remaining credit headroom (null = unknown, no numeric cap).
        public decimal? AvailableCredit { get; set; }

        public Task<CartValidateResponse> ValidateCartAsync(CartValidateRequest r, string c, CancellationToken ct = default) => Task.FromResult(new CartValidateResponse());

        public Task<(bool Reserved, ReserveResponse Response)> ReserveAsync(ReserveRequest r, string c, CancellationToken ct = default)
        {
            var response = new ReserveResponse();
            response.ReservationIds.Add("RSV-1"); // the gate's soft reservation — payment reads it server-side
            return Task.FromResult((true, response));
        }

        public Task ReleaseAsync(ReleaseRequest r, string c, CancellationToken ct = default) => Task.CompletedTask;

        public Task<CartPricingResult> ResolvePricingAsync(PricingResolveRequest r, string c, CancellationToken ct = default)
        {
            var lines = r.Lines.Select(l => new PricedLine(l.ItemNumber, l.Quantity, UnitPrice, UnitPrice * l.Quantity)).ToList();
            return Task.FromResult(new CartPricingResult("C-1", Decision.ToString(), Decision, lines, AvailableCredit));
        }

        public Task<OrderStatusResponse> SubmitOrderAsync(OrderRequest r, string c, CancellationToken ct = default)
        {
            OrderSubmitted = true;
            SubmittedOrder = r;
            return Task.FromResult(OrderAck);
        }

        public Task<OrderStatusResponse?> GetOrderStatusAsync(string id, string c, CancellationToken ct = default) => Task.FromResult<OrderStatusResponse?>(null);
    }

    private (HttpClient Client, FakeMiddlewareApi Middleware) Build()
    {
        var middleware = new FakeMiddlewareApi();
        var catalog = new FakeCatalogApi(new CatalogProduct("PART-1", "Brake Pad", "desc", "Brakes", "active", new CatalogProductMetafields("ea", 1, 1, false)));
        var client = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<ICatalogApi>(_ => catalog);
            s.AddScoped<IMiddlewareApi>(_ => middleware);
        })).CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthenticationHandler.CustomerHeader, "C-1");
        return (client, middleware);
    }

    [Fact]
    public async Task Successful_payment_submits_the_order()
    {
        var (client, middleware) = Build();
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });
        await client.PostAsJsonAsync("/api/checkout/start", new { }); // run the gate → server holds the reservation set

        var response = await client.PostAsJsonAsync("/api/checkout/pay",
            new { amount = 39.90m, currency = "GBP", paymentToken = "ok", reservationIds = new[] { "RSV-1" } });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("OrderPlaced", result.GetProperty("status").GetString());
        Assert.Equal("SO-1", result.GetProperty("orderReference").GetString());
        Assert.True(middleware.OrderSubmitted);
    }

    [Fact]
    public async Task Payment_locks_server_resolved_prices_on_the_order()
    {
        var (client, middleware) = Build();
        middleware.UnitPrice = 12.50m;
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 3m, site = "1" });
        await client.PostAsJsonAsync("/api/checkout/start", new { }); // run the gate → server holds the reservation set

        // The client sends amount 0 deliberately — the BFF must resolve the price server-side.
        var response = await client.PostAsJsonAsync("/api/checkout/pay",
            new { amount = 0m, currency = "GBP", paymentToken = "ok", reservationIds = new[] { "RSV-1" } });
        response.EnsureSuccessStatusCode();

        var line = Assert.Single(middleware.SubmittedOrder!.Lines);
        Assert.Equal(12.50, line.LockedPrice.Amount, 3); // resolved unit price, not the client's 0
        Assert.Equal("GBP", line.LockedPrice.Currency);
    }

    [Fact]
    public async Task Declined_payment_does_not_submit_an_order()
    {
        var (client, middleware) = Build();
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });
        await client.PostAsJsonAsync("/api/checkout/start", new { }); // run the gate → server holds the reservation set

        var response = await client.PostAsJsonAsync("/api/checkout/pay",
            new { amount = 39.90m, currency = "GBP", paymentToken = "decline", reservationIds = new[] { "RSV-1" } });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("PaymentFailed", result.GetProperty("status").GetString());
        Assert.False(middleware.OrderSubmitted);
    }

    [Fact]
    public async Task Approved_credit_can_pay_on_account_without_a_card_charge()
    {
        var (client, middleware) = Build();
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });
        await client.PostAsJsonAsync("/api/checkout/start", new { }); // run the gate → server holds the reservation set

        // paymentToken "decline" would fail a card charge — on-account must bypass the card entirely.
        var response = await client.PostAsJsonAsync("/api/checkout/pay", new
        {
            amount = 39.90m,
            currency = "GBP",
            paymentToken = "decline",
            reservationIds = new[] { "RSV-1" },
            paymentMethod = "OnAccount",
            poNumber = "PO-12345",
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("OrderPlaced", result.GetProperty("status").GetString());
        Assert.True(middleware.OrderSubmitted);
        Assert.Equal("OnAccount", middleware.SubmittedOrder!.PaymentMethod);
        Assert.Equal("PO-12345", middleware.SubmittedOrder!.PurchaseOrderNumber);
    }

    [Fact]
    public async Task On_account_is_refused_when_credit_is_not_approved()
    {
        var (client, middleware) = Build();
        middleware.Decision = CreditDecision.RequiresApproval; // over-limit — not approved for net terms
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });
        await client.PostAsJsonAsync("/api/checkout/start", new { }); // run the gate → server holds the reservation set

        var response = await client.PostAsJsonAsync("/api/checkout/pay", new
        {
            amount = 39.90m,
            currency = "GBP",
            paymentToken = "ok",
            reservationIds = new[] { "RSV-1" },
            paymentMethod = "OnAccount",
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("CreditDeclined", result.GetProperty("status").GetString());
        Assert.False(middleware.OrderSubmitted); // no order placed on a refused on-account attempt
    }

    [Fact]
    public async Task On_account_is_refused_when_the_order_exceeds_remaining_credit()
    {
        var (client, middleware) = Build();
        middleware.UnitPrice = 100m; // 2 × 100 = 200 net
        middleware.AvailableCredit = 50m; // headroom well below the order total
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });
        await client.PostAsJsonAsync("/api/checkout/start", new { });

        var response = await client.PostAsJsonAsync("/api/checkout/pay", new
        {
            amount = 200m,
            currency = "GBP",
            paymentToken = "ok",
            reservationIds = new[] { "RSV-1" },
            paymentMethod = "OnAccount",
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("CreditDeclined", result.GetProperty("status").GetString());
        Assert.False(middleware.OrderSubmitted);
    }

    [Fact]
    public async Task Changing_the_cart_after_the_gate_rejects_payment()
    {
        var (client, middleware) = Build();
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });
        await client.PostAsJsonAsync("/api/checkout/start", new { }); // reserve the cart as it is now
        // Add another line AFTER the gate — the order must not be placed with this unreserved line.
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 1m, site = "1" });

        var response = await client.PostAsJsonAsync("/api/checkout/pay",
            new { amount = 39.90m, currency = "GBP", paymentToken = "ok", reservationIds = new[] { "RSV-1" } });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("CartChanged", result.GetProperty("status").GetString());
        Assert.False(middleware.OrderSubmitted);
    }

    [Fact]
    public async Task Card_order_carries_payment_method_and_a_stable_idempotency_key()
    {
        var (client, middleware) = Build();
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });
        await client.PostAsJsonAsync("/api/checkout/start", new { }); // run the gate → server holds the reservation set

        // The client sends a FORGED reservation id — the server must ignore it and use the set it
        // placed at the gate ("RSV-1"), so the order + key are derived from the server's reservation.
        await client.PostAsJsonAsync("/api/checkout/pay",
            new { amount = 39.90m, currency = "GBP", paymentToken = "ok", reservationIds = new[] { "CLIENT-FORGED" } });

        var order = middleware.SubmittedOrder!;
        Assert.Equal("Card", order.PaymentMethod);
        Assert.Equal(new[] { "RSV-1" }, order.ReservationIds); // server's reservation, not the client's forgery
        // Stable SHA-256-derived key (64 hex chars), not the old random per-call Guid (32 chars).
        Assert.Equal(64, order.IdempotencyKey.Length);
        Assert.Equal(
            PartsPortal.Bff.Payments.PaymentService.DeriveIdempotencyKey("C-1", new[] { "RSV-1" }),
            order.IdempotencyKey);
    }

    [Fact]
    public async Task Paying_without_running_the_gate_is_rejected_and_places_no_order()
    {
        var (client, middleware) = Build();
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });
        // No /checkout/start — there is no server-held reservation set, so the client cannot bypass
        // reserve-before-commit by supplying its own reservation ids.

        var response = await client.PostAsJsonAsync("/api/checkout/pay",
            new { amount = 39.90m, currency = "GBP", paymentToken = "ok", reservationIds = new[] { "FORGED-1" } });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("NoReservation", result.GetProperty("status").GetString());
        Assert.False(middleware.OrderSubmitted);
    }

    private sealed class SpyEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Order_confirmation_email_is_sent_when_an_order_is_placed()
    {
        var middleware = new FakeMiddlewareApi();
        var spy = new SpyEmailSender();
        var client = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<ICatalogApi>(_ => new FakeCatalogApi(new CatalogProduct("PART-1", "Brake Pad", "desc", "Brakes", "active", new CatalogProductMetafields("ea", 1, 1, false))));
            s.AddScoped<IMiddlewareApi>(_ => middleware);
            s.AddSingleton<IEmailSender>(spy);
        })).CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthenticationHandler.CustomerHeader, "C-1");

        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });
        await client.PostAsJsonAsync("/api/checkout/start", new { });
        await client.PostAsJsonAsync("/api/checkout/pay",
            new { amount = 39.90m, currency = "GBP", paymentToken = "ok", reservationIds = new[] { "RSV-1" } });

        var sent = Assert.Single(spy.Sent);
        Assert.Equal("c-1@example.com", sent.To); // the dev handler sets {customer}@example.com
        Assert.Contains("SO-1", sent.Subject); // the order reference
    }

    [Fact]
    public void Idempotency_key_is_deterministic_order_insensitive_and_customer_scoped()
    {
        static string Key(string c, string[] r) => PartsPortal.Bff.Payments.PaymentService.DeriveIdempotencyKey(c, r);

        // Same customer + same reservation set (any order) → same key (a retry de-dups).
        Assert.Equal(Key("C-1", new[] { "RSV-1", "RSV-2" }), Key("C-1", new[] { "RSV-2", "RSV-1" }));
        // Different reservation set or different customer → different key.
        Assert.NotEqual(Key("C-1", new[] { "RSV-1" }), Key("C-1", new[] { "RSV-2" }));
        Assert.NotEqual(Key("C-1", new[] { "RSV-1" }), Key("C-2", new[] { "RSV-1" }));
    }
}
