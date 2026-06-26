using System.Net;
using System.Net.Http.Json;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Models;
using PartsPortal.Shared.Pricing;

namespace PartsPortal.Bff.Clients;

/// <summary>
/// Calls the integration middleware (via APIM) for the checkout gate: availability
/// validate/reserve/release (TDD §4.6), pricing/credit resolve (§4.5), and order submit/status.
/// Reuses the generated contract DTOs. Correlation id is propagated on every call (TDD §11).
/// </summary>
public interface IMiddlewareApi
{
    Task<CartValidateResponse> ValidateCartAsync(CartValidateRequest request, string correlationId, CancellationToken ct = default);

    /// <summary>Returns whether the whole cart was reserved (false ⇒ 409 shortfall), plus the response.</summary>
    Task<(bool Reserved, ReserveResponse Response)> ReserveAsync(ReserveRequest request, string correlationId, CancellationToken ct = default);

    Task ReleaseAsync(ReleaseRequest request, string correlationId, CancellationToken ct = default);

    Task<CartPricingResult> ResolvePricingAsync(PricingResolveRequest request, string correlationId, CancellationToken ct = default);

    Task<OrderStatusResponse> SubmitOrderAsync(OrderRequest request, string correlationId, CancellationToken ct = default);

    Task<OrderStatusResponse?> GetOrderStatusAsync(string orderId, string correlationId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class MiddlewareApi(IHttpClientFactory httpClientFactory) : IMiddlewareApi
{
    private HttpClient Client(string correlationId)
    {
        var client = httpClientFactory.CreateClient(BffClients.Middleware);
        client.DefaultRequestHeaders.Remove(CorrelationContext.HeaderName);
        client.DefaultRequestHeaders.Add(CorrelationContext.HeaderName, correlationId);
        return client;
    }

    public async Task<CartValidateResponse> ValidateCartAsync(CartValidateRequest request, string correlationId, CancellationToken ct = default)
    {
        using var response = await Client(correlationId).PostAsJsonAsync("cart/validate", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartValidateResponse>(ct))!;
    }

    public async Task<(bool Reserved, ReserveResponse Response)> ReserveAsync(ReserveRequest request, string correlationId, CancellationToken ct = default)
    {
        using var response = await Client(correlationId).PostAsJsonAsync("cart/reserve", request, ct);
        var reserved = response.StatusCode != HttpStatusCode.Conflict;
        if (reserved)
        {
            response.EnsureSuccessStatusCode();
        }

        return (reserved, (await response.Content.ReadFromJsonAsync<ReserveResponse>(ct))!);
    }

    public async Task ReleaseAsync(ReleaseRequest request, string correlationId, CancellationToken ct = default)
    {
        using var response = await Client(correlationId).PostAsJsonAsync("cart/release", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<CartPricingResult> ResolvePricingAsync(PricingResolveRequest request, string correlationId, CancellationToken ct = default)
    {
        using var response = await Client(correlationId).PostAsJsonAsync("pricing/resolve", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartPricingResult>(ct))!;
    }

    public async Task<OrderStatusResponse> SubmitOrderAsync(OrderRequest request, string correlationId, CancellationToken ct = default)
    {
        using var response = await Client(correlationId).PostAsJsonAsync("order", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderStatusResponse>(ct))!;
    }

    public async Task<OrderStatusResponse?> GetOrderStatusAsync(string orderId, string correlationId, CancellationToken ct = default)
    {
        using var response = await Client(correlationId).GetAsync($"order/{Uri.EscapeDataString(orderId)}/status", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderStatusResponse>(ct);
    }
}
