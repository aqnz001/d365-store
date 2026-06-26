using System.Net;
using System.Net.Http.Json;
using PartsPortal.Shared.Http;

namespace PartsPortal.Shared.Writeback;

/// <summary>
/// Calls FinOps OData over the resilient, config-driven "odata" HttpClient (T4; transient
/// 503s are retried with backoff before surfacing). Header → lines, linked by order number.
/// </summary>
public sealed class ODataOrderClient(IHttpClientFactory httpClientFactory) : IODataOrderClient
{
    private HttpClient Client => httpClientFactory.CreateClient(ResilientHttpClientExtensions.ODataClient);

    public async Task<string> CreateHeaderAsync(string customerAccount, CancellationToken ct = default)
    {
        using var response = await Client.PostAsJsonAsync("data/SalesOrderHeadersV2", new { customerAccount }, ct);
        await ThrowIfFailedAsync(response);
        var header = await response.Content.ReadFromJsonAsync<HeaderResult>(ct);
        return header!.SalesOrderNumber;
    }

    public async Task CreateLineAsync(string salesOrderNumber, string itemNumber, decimal quantity, CancellationToken ct = default)
    {
        using var response = await Client.PostAsJsonAsync(
            "data/SalesOrderLines", new { salesOrderNumber, itemNumber, quantity }, ct);
        await ThrowIfFailedAsync(response);
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
}
