using System.Net;
using System.Net.Http.Json;
using PartsPortal.Shared.Contracts.Messages;
using PartsPortal.Shared.Http;

namespace PartsPortal.Shared.Writeback;

/// <summary>
/// Calls FinOps OData over the resilient, config-driven "odata" HttpClient (T4; transient
/// 503s are retried with backoff before surfacing). Header → lines, linked by order number.
/// </summary>
public sealed class ODataOrderClient(IHttpClientFactory httpClientFactory) : IODataOrderClient
{
    private HttpClient Client => httpClientFactory.CreateClient(ResilientHttpClientExtensions.ODataClient);

    public async Task<string> CreateHeaderAsync(OrderInboundMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var header = new
        {
            customerAccount = message.CustomerAccount,
            currency = message.Currency,
            paymentMethod = string.IsNullOrWhiteSpace(message.PaymentMethod) ? "Card" : message.PaymentMethod,
            purchaseOrderNumber = message.PurchaseOrderNumber,
            correlationId = message.CorrelationId,
            idempotencyReference = message.IdempotencyKey,
        };
        using var response = await Client.PostAsJsonAsync("data/SalesOrderHeadersV2", header, ct);
        await ThrowIfFailedAsync(response);
        var result = await response.Content.ReadFromJsonAsync<HeaderResult>(ct);
        return result!.SalesOrderNumber;
    }

    public async Task CreateLineAsync(string salesOrderNumber, string itemNumber, decimal quantity, CancellationToken ct = default)
    {
        using var response = await Client.PostAsJsonAsync(
            "data/SalesOrderLines", new { salesOrderNumber, itemNumber, quantity }, ct);
        await ThrowIfFailedAsync(response);
    }

    public async Task<decimal?> GetCurrentPriceAsync(string itemNumber, CancellationToken ct = default)
    {
        using var response = await Client.GetAsync($"data/SalesPrices?itemNumber={Uri.EscapeDataString(itemNumber)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // no price on record — caller skips the integrity check
        }

        await ThrowIfFailedAsync(response);
        var price = await response.Content.ReadFromJsonAsync<PriceResult>(ct);
        return price?.Price;
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync();
        throw response.StatusCode == HttpStatusCode.ServiceUnavailable
            ? new TransientWritebackException(detail)
            : new PermanentWritebackException(detail);
    }

    private sealed record HeaderResult(string SalesOrderNumber, string CustomerAccount, string Status);
    private sealed record PriceResult(string ItemNumber, decimal Price, string Currency);
}
