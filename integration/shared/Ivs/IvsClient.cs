using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using PartsPortal.Shared.Http;

namespace PartsPortal.Shared.Ivs;

/// <summary>
/// Talks to IVS over the resilient, config-driven "ivs" HttpClient (T4). Uses thin internal
/// adapter records matching the IVS wire shape (integration/contracts/openapi/ivs.yaml; the
/// generated DTOs remain the validated contract reference). Reserve returns a 200 reservation
/// or a 409 shortfall — branched on status here.
/// </summary>
public sealed class IvsClient(IHttpClientFactory httpClientFactory, IOptions<IvsOptions> options) : IIvsClient
{
    private readonly IvsOptions _options = options.Value;

    private HttpClient Client => httpClientFactory.CreateClient(ResilientHttpClientExtensions.IvsClient);

    public async Task<AtpResult> QueryAtpAsync(string productId, string site, string location, CancellationToken ct = default)
    {
        var body = new IndexQueryBody([new DimensionBody(productId, site, location)], ReturnNegative: false);
        using var response = await Client.PostAsJsonAsync(
            $"api/environment/{_options.EnvironmentId}/onhand/indexquery?QueryATP=true", body, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IndexQueryResult>(ct);
        var row = result?.Results is { Count: > 0 } rows ? rows[0] : null;
        return new AtpResult(row?.Afr ?? 0m, row?.Atp ?? 0m);
    }

    public async Task<IvsReserveResult> ReserveAsync(string productId, string site, string location, decimal quantity, CancellationToken ct = default)
    {
        var body = new ReserveBody(productId, site, location, quantity, IfCheckAvailForReserv: true);
        using var response = await Client.PostAsJsonAsync(
            $"api/environment/{_options.EnvironmentId}/onhand/reserve", body, ct);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var shortfall = await response.Content.ReadFromJsonAsync<ShortfallBody>(ct);
            return new IvsReserveResult(false, null, shortfall?.AvailableQuantity ?? 0m);
        }

        response.EnsureSuccessStatusCode();
        var ok = await response.Content.ReadFromJsonAsync<ReserveOkBody>(ct);
        return new IvsReserveResult(true, ok?.ReservationId, ok?.ReservedQuantity ?? quantity);
    }

    public async Task ReleaseAsync(string reservationId, CancellationToken ct = default)
    {
        using var response = await Client.PostAsJsonAsync(
            $"api/environment/{_options.EnvironmentId}/onhand/release", new ReleaseBody(reservationId), ct);
        response.EnsureSuccessStatusCode();
    }

    // Internal IVS wire adapter records (match ivs-sim / ivs.yaml).
    private sealed record DimensionBody(string ProductId, string Site, string Location);
    private sealed record IndexQueryBody(IReadOnlyList<DimensionBody> Products, bool ReturnNegative);
    private sealed record OnHandRow(string ProductId, string Site, string Location, decimal Afr, decimal? Atp);
    private sealed record IndexQueryResult(string EnvironmentId, IReadOnlyList<OnHandRow> Results);
    private sealed record ReserveBody(string ProductId, string Site, string Location, decimal Quantity, bool IfCheckAvailForReserv);
    private sealed record ReserveOkBody(string Status, string ReservationId, decimal ReservedQuantity);
    private sealed record ShortfallBody(string Status, decimal RequestedQuantity, decimal AvailableQuantity, string? Message);
    private sealed record ReleaseBody(string ReservationId);
}
