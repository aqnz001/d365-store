// IVS simulator (Inventory Visibility Service stand-in) — Phase 1 mock.
//
// This service emulates the Inventory Visibility Service: the SOLE authority for
// inventory availability and reservation. It is deterministic and seedable so tests
// are repeatable, and it can inject shortfall to exercise negative paths (Handover §8).
//
// IMPORTANT SEMANTICS:
//   * We track AFR (Available-For-Reservation) and ATP (Available-To-Promise) per
//     (product, site, location). We NEVER expose raw on-hand — callers receive
//     ATP/AFR only, surfaced upstream as availability bands.
//   * Reserve/release adjust AFR here; in production this is IVS, never a side store.
//
// Phase 2: this whole process is replaced by the real IVS sandbox. Endpoints/shapes
// here mirror TDD §4.1–4.3 so no caller change is required.

using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// --- Configuration (no literals in logic; everything comes from config) -------------
// Simulate:Shortfall — when true, reserve calls report a shortfall regardless of AFR.
// Override per request with header: x-sim-shortfall: true|false
var configShortfall = app.Configuration.GetValue("Simulate:Shortfall", false);

// --- In-memory state ----------------------------------------------------------------
// Keyed by composite (product|site|location). AFR is the reservable quantity.
// ATP is derived/seeded; we never store or surface raw on-hand.
var inventory = new ConcurrentDictionary<string, InventoryRecord>();
// reservationId -> the dimension key + qty, so release can restore AFR.
var reservations = new ConcurrentDictionary<string, ReservationRecord>();

static string Key(string product, string site, string location) =>
    $"{product}|{site}|{location}";

// Resolve the effective shortfall flag: per-request header overrides config.
bool ShortfallActive(HttpRequest req)
{
    if (req.Headers.TryGetValue("x-sim-shortfall", out var hdr)
        && bool.TryParse(hdr.ToString(), out var parsed))
    {
        return parsed;
    }
    return configShortfall;
}

// ====================================================================================
// TDD §4.1 — Index query: ATP/AFR per product/dimension.
//   POST /api/environment/{environmentId}/onhand/indexquery?QueryATP=true
//   Body: { "products": [ { "productId", "site", "location" }, ... ] }
//   returnNegative: when false (default), negative ATP/AFR is clamped to 0.
// ====================================================================================
app.MapPost("/api/environment/{environmentId}/onhand/indexquery",
    (string environmentId, bool? QueryATP, IndexQueryRequest body) =>
{
    var returnNegative = body.ReturnNegative ?? false;
    var includeAtp = QueryATP ?? true;

    var results = new List<OnHandResult>();
    foreach (var dim in body.Products ?? new List<DimensionRef>())
    {
        var key = Key(dim.ProductId, dim.Site, dim.Location);
        inventory.TryGetValue(key, out var rec);

        var afr = rec?.Afr ?? 0m;
        var atp = rec?.Atp ?? 0m;
        if (!returnNegative)
        {
            afr = Math.Max(0m, afr);
            atp = Math.Max(0m, atp);
        }

        results.Add(new OnHandResult(
            dim.ProductId, dim.Site, dim.Location,
            afr,
            includeAtp ? atp : null));
    }

    return Results.Ok(new IndexQueryResponse(environmentId, results));
});

// ====================================================================================
// TDD §4.2 — Reserve.
//   POST /api/environment/{environmentId}/onhand/reserve
//   ifCheckAvailForReserv: when true, validate AFR before committing.
//   Success: decrement AFR, return a generated reservation id.
//   Shortfall (config/header or insufficient AFR): documented shortfall response.
// ====================================================================================
app.MapPost("/api/environment/{environmentId}/onhand/reserve",
    (string environmentId, HttpRequest httpReq, ReserveRequest body) =>
{
    var key = Key(body.ProductId, body.Site, body.Location);
    inventory.TryGetValue(key, out var rec);
    var afr = rec?.Afr ?? 0m;

    var forceShortfall = ShortfallActive(httpReq);
    var insufficient = body.IfCheckAvailForReserv && body.Quantity > afr;

    if (forceShortfall || insufficient)
    {
        // Documented shortfall response: caller must not commit the order line.
        // availableQuantity tells the caller how much (if any) could be reserved.
        var available = forceShortfall ? 0m : Math.Max(0m, afr);
        return Results.Json(new ShortfallResponse(
            Status: "Shortfall",
            ProductId: body.ProductId,
            Site: body.Site,
            Location: body.Location,
            RequestedQuantity: body.Quantity,
            AvailableQuantity: available,
            Message: forceShortfall
                ? "Shortfall injected by simulator (Simulate:Shortfall / x-sim-shortfall)."
                : "Requested quantity exceeds Available-For-Reservation (AFR)."),
            statusCode: StatusCodes.Status409Conflict);
    }

    // Commit: decrement AFR (IVS is the sole reservation authority) and mint an id.
    // Reservation id is deterministic-friendly but unique per call via Guid.
    var reservationId = $"RSV-{Guid.NewGuid():N}";
    inventory.AddOrUpdate(key,
        // Should not occur (we already read it), but seed defensively at 0 baseline.
        _ => new InventoryRecord(0m, 0m),
        (_, existing) => existing with { Afr = existing.Afr - body.Quantity });

    reservations[reservationId] = new ReservationRecord(
        key, body.ProductId, body.Site, body.Location, body.Quantity);

    return Results.Ok(new ReserveResponse(
        Status: "Reserved",
        ReservationId: reservationId,
        ProductId: body.ProductId,
        Site: body.Site,
        Location: body.Location,
        ReservedQuantity: body.Quantity));
});

// ====================================================================================
// TDD §4.3 — Release: restore AFR for a reservation id (idempotent on unknown ids).
//   POST /api/environment/{environmentId}/onhand/release
// ====================================================================================
app.MapPost("/api/environment/{environmentId}/onhand/release",
    (string environmentId, ReleaseRequest body) =>
{
    if (!reservations.TryRemove(body.ReservationId, out var res))
    {
        // Idempotent: releasing an unknown/already-released id is a no-op success.
        return Results.Ok(new ReleaseResponse(
            Status: "NoOp",
            ReservationId: body.ReservationId,
            RestoredQuantity: 0m));
    }

    inventory.AddOrUpdate(res.Key,
        _ => new InventoryRecord(res.Quantity, res.Quantity),
        (_, existing) => existing with { Afr = existing.Afr + res.Quantity });

    return Results.Ok(new ReleaseResponse(
        Status: "Released",
        ReservationId: body.ReservationId,
        RestoredQuantity: res.Quantity));
});

// ====================================================================================
// Allocation — ring-fence a pool, reducing AFR (e.g. for a channel/customer).
//   POST /api/environment/{environmentId}/allocation
// Reduces AFR (and ATP) for the dimension by the allocated quantity. Does not mint a
// reservation id; this models a standing carve-out, not a checkout reservation.
// ====================================================================================
app.MapPost("/api/environment/{environmentId}/allocation",
    (string environmentId, AllocationRequest body) =>
{
    var key = Key(body.ProductId, body.Site, body.Location);
    inventory.AddOrUpdate(key,
        _ => new InventoryRecord(-body.Quantity, -body.Quantity),
        (_, existing) => existing with
        {
            Afr = existing.Afr - body.Quantity,
            Atp = existing.Atp - body.Quantity
        });

    inventory.TryGetValue(key, out var updated);
    return Results.Ok(new AllocationResponse(
        Status: "Allocated",
        ProductId: body.ProductId,
        Site: body.Site,
        Location: body.Location,
        AllocatedQuantity: body.Quantity,
        RemainingAfr: Math.Max(0m, updated?.Afr ?? 0m)));
});

// ====================================================================================
// Admin: deterministic seeding. Sets AFR (and ATP) for items so tests are repeatable.
//   POST /admin/seed
//   Body: { "items": [ { "productId", "site", "location", "afr", "atp" }, ... ] }
// ====================================================================================
app.MapPost("/admin/seed", (SeedRequest body) =>
{
    var count = 0;
    foreach (var item in body.Items ?? new List<SeedItem>())
    {
        var key = Key(item.ProductId, item.Site, item.Location);
        // ATP defaults to AFR when not supplied.
        inventory[key] = new InventoryRecord(item.Afr, item.Atp ?? item.Afr);
        count++;
    }
    return Results.Ok(new { seeded = count });
});

// Liveness probe for orchestration/CI.
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "ivs-sim" }));

app.Run();

// --- Contracts (mock-local; the real shapes live in integration/contracts) ----------

record InventoryRecord(decimal Afr, decimal Atp);
record ReservationRecord(string Key, string ProductId, string Site, string Location, decimal Quantity);

record DimensionRef(string ProductId, string Site, string Location);
record IndexQueryRequest(List<DimensionRef>? Products, bool? ReturnNegative);
record OnHandResult(string ProductId, string Site, string Location, decimal Afr, decimal? Atp);
record IndexQueryResponse(string EnvironmentId, List<OnHandResult> Results);

record ReserveRequest(string ProductId, string Site, string Location, decimal Quantity, bool IfCheckAvailForReserv);
record ReserveResponse(string Status, string ReservationId, string ProductId, string Site, string Location, decimal ReservedQuantity);
record ShortfallResponse(string Status, string ProductId, string Site, string Location, decimal RequestedQuantity, decimal AvailableQuantity, string Message);

record ReleaseRequest(string ReservationId);
record ReleaseResponse(string Status, string ReservationId, decimal RestoredQuantity);

record AllocationRequest(string ProductId, string Site, string Location, decimal Quantity);
record AllocationResponse(string Status, string ProductId, string Site, string Location, decimal AllocatedQuantity, decimal RemainingAfr);

record SeedItem(string ProductId, string Site, string Location, decimal Afr, decimal? Atp);
record SeedRequest(List<SeedItem>? Items);
