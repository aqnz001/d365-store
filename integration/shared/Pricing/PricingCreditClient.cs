using System.Net.Http.Json;
using PartsPortal.Shared.Http;

namespace PartsPortal.Shared.Pricing;

/// <summary>
/// Calls the pricing/credit service over the resilient, config-driven "pricing-credit"
/// HttpClient (T4). Thin internal adapter records match the wire shape
/// (integration/contracts/openapi/pricing-credit.yaml; the generated DTOs remain the
/// validated contract reference).
/// </summary>
public sealed class PricingCreditClient(IHttpClientFactory httpClientFactory) : IPricingCreditClient
{
    public async Task<PricingResult> ResolveAsync(string customerAccount, IReadOnlyList<PricingResolveLine> lines, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var client = httpClientFactory.CreateClient(ResilientHttpClientExtensions.PricingCreditClient);
        var body = new ResolveBody(customerAccount, lines.Select(l => new LineBody(l.ItemNumber, l.Quantity)).ToList());

        using var response = await client.PostAsJsonAsync("api/services/PortalPricing/resolve", body, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ResolveResultBody>(ct);
        var priced = (result?.Lines ?? [])
            // Tax is FinOps-owned: carry through whatever it returns (absent → 0); the portal never computes it.
            .Select(l => new PricedLine(l.ItemNumber, l.Quantity, l.UnitPrice, l.NetEffectivePrice, l.TaxRate ?? 0m, l.TaxAmount ?? 0m))
            .ToList();

        // Unknown/absent credit status defaults to "hold" so the service blocks rather than over-permits.
        return new PricingResult(result?.CustomerAccount ?? customerAccount, result?.CreditStatus ?? "hold", priced);
    }

    private sealed record LineBody(string ItemNumber, decimal Quantity);
    private sealed record ResolveBody(string CustomerAccount, IReadOnlyList<LineBody> Lines);
    private sealed record ResolvedLineBody(string ItemNumber, decimal Quantity, decimal UnitPrice, decimal NetEffectivePrice, decimal? TaxRate, decimal? TaxAmount);
    private sealed record ResolveResultBody(string CustomerAccount, string CreditStatus, IReadOnlyList<ResolvedLineBody> Lines);
}
