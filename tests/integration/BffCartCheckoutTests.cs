using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PartsPortal.Bff;
using PartsPortal.Bff.Auth;
using PartsPortal.Bff.Cart;
using PartsPortal.Bff.Clients;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Pricing;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// S3+S4 — the BFF cart (order-rule validation on add) and checkout gate (availability →
/// price/credit → reserve), with the catalog + middleware clients stubbed.
/// </summary>
public class BffCartCheckoutTests(WebApplicationFactory<BffApp> factory) : IClassFixture<WebApplicationFactory<BffApp>>
{
    private sealed class FakeCatalogApi(params CatalogProduct[] products) : ICatalogApi
    {
        public Task<IReadOnlyList<CatalogProduct>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CatalogProduct>>(products);

        public Task<CatalogProduct?> GetAsync(string sku, CancellationToken ct = default) => Task.FromResult(products.FirstOrDefault(p => p.Sku == sku));
    }

    private sealed class FakeMiddlewareApi : IMiddlewareApi
    {
        public CartValidateResponse Validate { get; set; } = new();
        public CartPricingResult Pricing { get; set; } = new("C-1", "OK", CreditDecision.Approved, []);
        public (bool Reserved, ReserveResponse Response) ReserveResult { get; set; } = (true, new ReserveResponse());

        public Task<CartValidateResponse> ValidateCartAsync(CartValidateRequest r, string c, CancellationToken ct = default) => Task.FromResult(Validate);
        public Task<(bool Reserved, ReserveResponse Response)> ReserveAsync(ReserveRequest r, string c, CancellationToken ct = default) => Task.FromResult(ReserveResult);
        public Task ReleaseAsync(ReleaseRequest r, string c, CancellationToken ct = default) => Task.CompletedTask;
        public Task<CartPricingResult> ResolvePricingAsync(PricingResolveRequest r, string c, CancellationToken ct = default) => Task.FromResult(Pricing);
        public Task<OrderStatusResponse> SubmitOrderAsync(OrderRequest r, string c, CancellationToken ct = default) => Task.FromResult(new OrderStatusResponse());
        public Task<OrderStatusResponse?> GetOrderStatusAsync(string id, string c, CancellationToken ct = default) => Task.FromResult<OrderStatusResponse?>(null);
    }

    private HttpClient Build(FakeCatalogApi catalog, FakeMiddlewareApi middleware)
    {
        var client = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<ICatalogApi>(_ => catalog);
            s.AddScoped<IMiddlewareApi>(_ => middleware);
        })).CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthenticationHandler.CustomerHeader, "C-1");
        return client;
    }

    private static CatalogProduct Product(string sku, decimal orderMultiple = 1, decimal minOrderQty = 1) =>
        new(sku, sku, "desc", "Brakes", "active", new CatalogProductMetafields("ea", orderMultiple, minOrderQty, false));

    private static CartValidateResponse AllowAll(string sku)
    {
        var response = new CartValidateResponse();
        response.Lines.Add(new CartValidateLineResult { ItemNumber = sku, Band = AvailabilityBand.InStock, Decision = LineDecision.Allow });
        return response;
    }

    [Fact]
    public async Task Add_item_enforces_order_multiple_and_min_qty()
    {
        var client = Build(new FakeCatalogApi(Product("PART-5", orderMultiple: 5, minOrderQty: 5)), new FakeMiddlewareApi());

        var ok = await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-5", quantity = 10m, site = "1" });
        ok.EnsureSuccessStatusCode();

        var badMultiple = await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-5", quantity = 7m, site = "1" });
        Assert.Equal(HttpStatusCode.BadRequest, badMultiple.StatusCode);

        var belowMin = await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-5", quantity = 0m, site = "1" });
        Assert.Equal(HttpStatusCode.BadRequest, belowMin.StatusCode);
    }

    [Fact]
    public async Task Checkout_is_ready_when_available_priced_and_reserved()
    {
        var middleware = new FakeMiddlewareApi { Validate = AllowAll("PART-1") };
        var reserve = new ReserveResponse();
        reserve.ReservationIds.Add("RSV-1");
        middleware.ReserveResult = (true, reserve);

        var client = Build(new FakeCatalogApi(Product("PART-1")), middleware);
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });

        var result = await (await client.PostAsync("/api/checkout/start", null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Ready", result.GetProperty("status").GetString());
        Assert.Equal("RSV-1", result.GetProperty("reservationIds")[0].GetString());
    }

    [Fact]
    public async Task Checkout_blocks_on_unavailable_line()
    {
        var response = new CartValidateResponse();
        response.Lines.Add(new CartValidateLineResult { ItemNumber = "PART-1", Band = AvailabilityBand.Unavailable, Decision = LineDecision.Block });
        var client = Build(new FakeCatalogApi(Product("PART-1")), new FakeMiddlewareApi { Validate = response });
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });

        var result = await (await client.PostAsync("/api/checkout/start", null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("AvailabilityBlocked", result.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Checkout_blocks_on_credit_hold()
    {
        var middleware = new FakeMiddlewareApi
        {
            Validate = AllowAll("PART-1"),
            Pricing = new CartPricingResult("C-1", "hold", CreditDecision.Blocked, []),
        };
        var client = Build(new FakeCatalogApi(Product("PART-1")), middleware);
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });

        var result = await (await client.PostAsync("/api/checkout/start", null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("CreditBlocked", result.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Checkout_reports_shortfall_when_reserve_fails()
    {
        var middleware = new FakeMiddlewareApi { Validate = AllowAll("PART-1"), ReserveResult = (false, new ReserveResponse()) };
        var client = Build(new FakeCatalogApi(Product("PART-1")), middleware);
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });

        var result = await (await client.PostAsync("/api/checkout/start", null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Shortfall", result.GetProperty("status").GetString());
    }
}
