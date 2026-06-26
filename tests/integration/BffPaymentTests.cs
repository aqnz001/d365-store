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

        public Task<CartValidateResponse> ValidateCartAsync(CartValidateRequest r, string c, CancellationToken ct = default) => Task.FromResult(new CartValidateResponse());
        public Task<(bool Reserved, ReserveResponse Response)> ReserveAsync(ReserveRequest r, string c, CancellationToken ct = default) => Task.FromResult((true, new ReserveResponse()));
        public Task ReleaseAsync(ReleaseRequest r, string c, CancellationToken ct = default) => Task.CompletedTask;
        public Task<CartPricingResult> ResolvePricingAsync(PricingResolveRequest r, string c, CancellationToken ct = default) => Task.FromResult(new CartPricingResult("C-1", "OK", CreditDecision.Approved, []));

        public Task<OrderStatusResponse> SubmitOrderAsync(OrderRequest r, string c, CancellationToken ct = default)
        {
            OrderSubmitted = true;
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

        var response = await client.PostAsJsonAsync("/api/checkout/pay",
            new { amount = 39.90m, currency = "GBP", paymentToken = "ok", reservationIds = new[] { "RSV-1" } });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("OrderPlaced", result.GetProperty("status").GetString());
        Assert.Equal("SO-1", result.GetProperty("orderReference").GetString());
        Assert.True(middleware.OrderSubmitted);
    }

    [Fact]
    public async Task Declined_payment_does_not_submit_an_order()
    {
        var (client, middleware) = Build();
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });

        var response = await client.PostAsJsonAsync("/api/checkout/pay",
            new { amount = 39.90m, currency = "GBP", paymentToken = "decline", reservationIds = new[] { "RSV-1" } });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("PaymentFailed", result.GetProperty("status").GetString());
        Assert.False(middleware.OrderSubmitted);
    }
}
