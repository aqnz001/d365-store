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

/// <summary>S6 — BFF account &amp; B2B: order history (after payment), credit standing, identity.</summary>
public class BffAccountTests(WebApplicationFactory<BffApp> factory) : IClassFixture<WebApplicationFactory<BffApp>>
{
    private sealed class FakeCatalogApi(params CatalogProduct[] products) : ICatalogApi
    {
        public Task<IReadOnlyList<CatalogProduct>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CatalogProduct>>(products);
        public Task<CatalogProduct?> GetAsync(string sku, CancellationToken ct = default) => Task.FromResult(products.FirstOrDefault(p => p.Sku == sku));
    }

    private sealed class FakeMiddlewareApi : IMiddlewareApi
    {
        public Task<CartValidateResponse> ValidateCartAsync(CartValidateRequest r, string c, CancellationToken ct = default) => Task.FromResult(new CartValidateResponse());
        public Task<(bool Reserved, ReserveResponse Response)> ReserveAsync(ReserveRequest r, string c, CancellationToken ct = default)
        {
            var response = new ReserveResponse();
            response.ReservationIds.Add("RSV-1");
            return Task.FromResult((true, response));
        }
        public Task ReleaseAsync(ReleaseRequest r, string c, CancellationToken ct = default) => Task.CompletedTask;
        public Task<CartPricingResult> ResolvePricingAsync(PricingResolveRequest r, string c, CancellationToken ct = default) => Task.FromResult(new CartPricingResult(r.CustomerAccount, "OK", CreditDecision.Approved, []));
        public Task<OrderStatusResponse> SubmitOrderAsync(OrderRequest r, string c, CancellationToken ct = default) => Task.FromResult(new OrderStatusResponse { OrderId = "ORD-1", SalesOrderNumber = "SO-1", Status = OrderStatus.Queued });
        public Task<OrderStatusResponse?> GetOrderStatusAsync(string id, string c, CancellationToken ct = default) => Task.FromResult<OrderStatusResponse?>(null);
    }

    private HttpClient Build()
    {
        var client = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<ICatalogApi>(_ => new FakeCatalogApi(new CatalogProduct("PART-1", "Brake Pad", "desc", "Brakes", "active", new CatalogProductMetafields("ea", 1, 1, false))));
            s.AddScoped<IMiddlewareApi>(_ => new FakeMiddlewareApi());
        })).CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthenticationHandler.CustomerHeader, "C-1");
        return client;
    }

    [Fact]
    public async Task Account_returns_the_customer()
    {
        var me = await Build().GetFromJsonAsync<JsonElement>("/api/account");
        Assert.Equal("C-1", me.GetProperty("customerAccount").GetString());
    }

    [Fact]
    public async Task Placed_order_appears_in_order_history()
    {
        var client = Build();
        await client.PostAsJsonAsync("/api/cart/items", new { itemNumber = "PART-1", quantity = 2m, site = "1" });
        await client.PostAsJsonAsync("/api/checkout/start", new { }); // run the gate so the server holds the reservation
        await client.PostAsJsonAsync("/api/checkout/pay", new { amount = 10m, currency = "GBP", paymentToken = "ok", reservationIds = new[] { "RSV-1" } });

        var orders = await client.GetFromJsonAsync<List<JsonElement>>("/api/account/orders");

        Assert.Single(orders!);
        Assert.Equal("SO-1", orders![0].GetProperty("orderReference").GetString());
    }

    [Fact]
    public async Task Credit_standing_is_resolved()
    {
        var credit = await Build().GetFromJsonAsync<JsonElement>("/api/account/credit");

        Assert.Equal("OK", credit.GetProperty("creditStatus").GetString());
        Assert.Equal("Approved", credit.GetProperty("decision").GetString());
    }
}
