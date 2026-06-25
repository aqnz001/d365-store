// FinOps OData writeback simulator — Phase 1 mock.
//
// Emulates the D365 FinOps OData entities used for sales-order writeback (TDD §4.4):
// SalesOrderHeadersV2 and SalesOrderLines. Deterministic + seedable, with transient
// failure injection to exercise retry/DLQ paths (Handover §8).
//
// Header -> lines ordering is enforced: a line is rejected unless its parent header
// exists. Lines referencing unknown master data (item/customer) are rejected with
// HTTP 400 to exercise the validation path.
//
// Phase 2: replaced by the FinOps sandbox; entity names/shapes mirror the contracts.

using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// --- Configuration (no literals in logic; everything comes from config) -------------
// Simulate:TransientFailureRate — probability [0..1] that a write returns HTTP 503.
// Override per request with header: x-sim-fail: transient
var transientFailureRate = app.Configuration.GetValue("Simulate:TransientFailureRate", 0d);

// --- In-memory state ----------------------------------------------------------------
// Seeded master data we will validate line references against.
var knownItems = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
var knownCustomers = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
// Created headers keyed by sales order number, so lines can enforce the FK.
var headers = new ConcurrentDictionary<string, HeaderRecord>(StringComparer.OrdinalIgnoreCase);
var lines = new ConcurrentDictionary<string, LineRecord>();

// Open Decision #3 — sales-order number ownership. We ASSUME FinOps owns the number
// sequence, so this mock generates the number on header create and returns it. If the
// decision lands elsewhere, change only this generator.
var orderSeq = 0;
string NextSalesOrderNumber() =>
    $"SO-{Interlocked.Increment(ref orderSeq):D6}";

// Deterministic transient-failure decision. Header forces it; otherwise sample the
// configured rate with a per-process RNG (rate 0 => never, 1 => always).
var rng = new Random(20260101);
bool ShouldFailTransient(HttpRequest req)
{
    if (req.Headers.TryGetValue("x-sim-fail", out var hdr)
        && string.Equals(hdr.ToString(), "transient", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }
    if (transientFailureRate <= 0d)
    {
        return false;
    }

    if (transientFailureRate >= 1d)
    {
        return true;
    }

    lock (rng) { return rng.NextDouble() < transientFailureRate; }
}

static IResult Transient() => Results.Json(
    new { error = new { code = "ServiceUnavailable", message = "Transient failure injected by simulator. Retry." } },
    statusCode: StatusCodes.Status503ServiceUnavailable);

// ====================================================================================
// TDD §4.4 — Sales order header create.
//   POST /data/SalesOrderHeadersV2
// Generates and returns a FinOps-owned sales order number (Open Decision #3).
// ====================================================================================
app.MapPost("/data/SalesOrderHeadersV2", (HttpRequest httpReq, HeaderRequest body) =>
{
    if (ShouldFailTransient(httpReq))
    {
        return Transient();
    }

    // Validate the customer reference against seeded master data.
    if (string.IsNullOrWhiteSpace(body.CustomerAccount)
        || !knownCustomers.ContainsKey(body.CustomerAccount))
    {
        return Results.Json(
            new { error = new { code = "InvalidCustomer", message = $"Unknown customer account '{body.CustomerAccount}'." } },
            statusCode: StatusCodes.Status400BadRequest);
    }

    var salesOrderNumber = NextSalesOrderNumber();
    headers[salesOrderNumber] = new HeaderRecord(salesOrderNumber, body.CustomerAccount);

    return Results.Json(new HeaderResponse(
        SalesOrderNumber: salesOrderNumber,
        CustomerAccount: body.CustomerAccount,
        Status: "Created"),
        statusCode: StatusCodes.Status201Created);
});

// ====================================================================================
// TDD §4.4 — Sales order line create.
//   POST /data/SalesOrderLines
// Requires a valid parent sales order number (FK -> header). Lines referencing missing
// master data (unknown item) are rejected with HTTP 400 to exercise validation.
// ====================================================================================
app.MapPost("/data/SalesOrderLines", (HttpRequest httpReq, LineRequest body) =>
{
    if (ShouldFailTransient(httpReq))
    {
        return Transient();
    }

    // FK: parent header must exist (header -> lines ordering).
    if (string.IsNullOrWhiteSpace(body.SalesOrderNumber)
        || !headers.ContainsKey(body.SalesOrderNumber))
    {
        return Results.Json(
            new { error = new { code = "MissingSalesOrder", message = $"No sales order header '{body.SalesOrderNumber}'. Create the header first." } },
            statusCode: StatusCodes.Status400BadRequest);
    }

    // Validation path: reject lines referencing unknown item master data.
    if (string.IsNullOrWhiteSpace(body.ItemNumber)
        || !knownItems.ContainsKey(body.ItemNumber))
    {
        return Results.Json(
            new { error = new { code = "InvalidItem", message = $"Unknown item number '{body.ItemNumber}'." } },
            statusCode: StatusCodes.Status400BadRequest);
    }

    var lineId = $"{body.SalesOrderNumber}:{Guid.NewGuid():N}";
    lines[lineId] = new LineRecord(lineId, body.SalesOrderNumber, body.ItemNumber, body.Quantity);

    return Results.Json(new LineResponse(
        LineId: lineId,
        SalesOrderNumber: body.SalesOrderNumber,
        ItemNumber: body.ItemNumber,
        Quantity: body.Quantity,
        Status: "Created"),
        statusCode: StatusCodes.Status201Created);
});

// ====================================================================================
// Admin: register known master data (items + customers) for validation.
//   POST /admin/seed
//   Body: { "items": ["ITEM-1", ...], "customers": ["CUST-1", ...] }
// ====================================================================================
app.MapPost("/admin/seed", (SeedRequest body) =>
{
    foreach (var item in body.Items ?? new List<string>())
    {
        knownItems[item] = 1;
    }

    foreach (var customer in body.Customers ?? new List<string>())
    {
        knownCustomers[customer] = 1;
    }

    return Results.Ok(new { items = knownItems.Count, customers = knownCustomers.Count });
});

// Liveness probe for orchestration/CI.
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "odata-sim" }));

app.Run();

// --- Contracts (mock-local; the real shapes live in integration/contracts) ----------

record HeaderRecord(string SalesOrderNumber, string CustomerAccount);
record LineRecord(string LineId, string SalesOrderNumber, string ItemNumber, decimal Quantity);

record HeaderRequest(string CustomerAccount);
record HeaderResponse(string SalesOrderNumber, string CustomerAccount, string Status);

record LineRequest(string SalesOrderNumber, string ItemNumber, decimal Quantity);
record LineResponse(string LineId, string SalesOrderNumber, string ItemNumber, decimal Quantity, string Status);

record SeedRequest(List<string>? Items, List<string>? Customers);

namespace PartsPortal.Mocks.ODataSim
{
    /// <summary>Public entry-point marker so WebApplicationFactory can host this app in tests.</summary>
    public sealed class ODataSimApp;
}
