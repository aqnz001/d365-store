// Pricing + credit resolution simulator — Phase 1 mock.
//
// Emulates the PortalPricing service (TDD §4.5): resolves a deterministic net effective
// price per line and reports the customer's credit status. Seedable per-item prices and
// per-customer credit make tests repeatable; credit status is configurable to exercise
// the over-limit / hold negative paths (Handover §8).
//
// Phase 2: replaced by the real pricing/credit service; request/response shapes mirror
// the contracts.

using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// --- Configuration (no literals in logic; everything comes from config) -------------
// Credit:Status — default credit verdict applied to every customer unless seeded.
// One of { OK, over-limit, hold }. Override per request with header x-sim-credit.
var defaultCreditStatus = app.Configuration.GetValue("Credit:Status", "OK") ?? "OK";
// Tax:Rate — VAT/GST fraction FinOps applies to each line (e.g. 0.20 = 20%). Stands in for the
// FinOps-owned tax the portal surfaces (it never computes tax itself). 0 disables tax.
var taxRate = app.Configuration.GetValue("Tax:Rate", 0.20m);

// --- In-memory state ----------------------------------------------------------------
// Seeded net effective unit price per item (deterministic).
var itemPrices = new ConcurrentDictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
// Seeded per-customer credit status overrides.
var customerCredit = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

// Resolve the effective credit status for a customer: header > per-customer seed > config.
string ResolveCreditStatus(HttpRequest req, string? customerAccount)
{
    if (req.Headers.TryGetValue("x-sim-credit", out var hdr) && !string.IsNullOrWhiteSpace(hdr))
    {
        return hdr.ToString();
    }

    if (!string.IsNullOrWhiteSpace(customerAccount)
        && customerCredit.TryGetValue(customerAccount, out var seeded))
    {
        return seeded;
    }

    return defaultCreditStatus;
}

// ====================================================================================
// TDD §4.5 — Resolve pricing + credit.
//   POST /api/services/PortalPricing/resolve
//   Body: { "customerAccount", "lines": [ { "itemNumber", "quantity" }, ... ] }
// Returns a deterministic net effective price per line plus the credit status.
// Unseeded items default to 0.00 (caller can treat as "needs price").
// ====================================================================================
app.MapPost("/api/services/PortalPricing/resolve",
    (HttpRequest httpReq, ResolveRequest body) =>
{
    var resolvedLines = new List<ResolvedLine>();
    foreach (var line in body.Lines ?? new List<RequestLine>())
    {
        var unitPrice = itemPrices.TryGetValue(line.ItemNumber, out var p) ? p : 0m;
        var netEffective = unitPrice * line.Quantity;
        var taxAmount = Math.Round(netEffective * taxRate, 2, MidpointRounding.AwayFromZero);
        resolvedLines.Add(new ResolvedLine(
            ItemNumber: line.ItemNumber,
            Quantity: line.Quantity,
            UnitPrice: unitPrice,
            NetEffectivePrice: netEffective,
            TaxRate: taxRate,
            TaxAmount: taxAmount));
    }

    var creditStatus = ResolveCreditStatus(httpReq, body.CustomerAccount);

    return Results.Ok(new ResolveResponse(
        CustomerAccount: body.CustomerAccount,
        CreditStatus: creditStatus,
        Lines: resolvedLines));
});

// ====================================================================================
// Admin: seed per-item prices and per-customer credit status.
//   POST /admin/seed
//   Body: { "prices": [ { "itemNumber", "unitPrice" } ], "credit": [ { "customerAccount", "status" } ] }
// ====================================================================================
app.MapPost("/admin/seed", (SeedRequest body) =>
{
    foreach (var price in body.Prices ?? new List<PriceSeed>())
    {
        itemPrices[price.ItemNumber] = price.UnitPrice;
    }

    foreach (var credit in body.Credit ?? new List<CreditSeed>())
    {
        customerCredit[credit.CustomerAccount] = credit.Status;
    }

    return Results.Ok(new { prices = itemPrices.Count, credit = customerCredit.Count });
});

// Liveness probe for orchestration/CI.
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "pricing-credit-sim" }));

app.Run();

// --- Contracts (mock-local; the real shapes live in integration/contracts) ----------

record RequestLine(string ItemNumber, decimal Quantity);
record ResolveRequest(string CustomerAccount, List<RequestLine>? Lines);

record ResolvedLine(string ItemNumber, decimal Quantity, decimal UnitPrice, decimal NetEffectivePrice, decimal TaxRate, decimal TaxAmount);
record ResolveResponse(string CustomerAccount, string CreditStatus, List<ResolvedLine> Lines);

record PriceSeed(string ItemNumber, decimal UnitPrice);
record CreditSeed(string CustomerAccount, string Status);
record SeedRequest(List<PriceSeed>? Prices, List<CreditSeed>? Credit);

namespace PartsPortal.Mocks.PricingCreditSim
{
    /// <summary>Public entry-point marker so WebApplicationFactory can host this app in tests.</summary>
    public sealed class PricingCreditSimApp;
}
