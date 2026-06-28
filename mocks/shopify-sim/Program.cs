// Shopify catalog simulator (Shopify Plus storefront stand-in) — Phase 1 mock.
//
// This service emulates the Shopify Admin product surface that the catalog sync
// (T5, BYOD -> Shopify, TDD §5.1) upserts into. It is in-memory and idempotent so
// repeated sync runs converge to the same state, and tests are repeatable.
//
// SEMANTICS:
//   * Products are keyed by SKU. PUT is an upsert: store-or-overwrite by sku.
//   * status mirrors the lifecycle mapping done by the sync — "active" for live
//     items, "archived" for delisted ones (e.g. Discontinued). The mock stores
//     whatever the caller sends; the lifecycle->status decision lives in the sync.
//
// Phase 2: this whole process is replaced by a real Shopify dev store (Open
// Decision #2). Endpoint shapes here mirror what the sync emits so no caller
// change is required.

using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// --- In-memory state ----------------------------------------------------------------
// Keyed by SKU. PUT overwrites the existing entry (idempotent upsert).
var products = new ConcurrentDictionary<string, ShopifyProduct>(StringComparer.Ordinal);

// ====================================================================================
// Upsert a product by SKU (TDD §5.1 — the sync's write target).
//   PUT /admin/products/{sku}
//   Body: { sku, title, bodyHtml, productType, status,
//           metafields: { unit, orderMultiple, minOrderQty, backorderable } }
// Store/overwrite by sku and echo back the stored product. Idempotent: re-PUTting
// the same body is a no-op convergence.
// ====================================================================================
app.MapPut("/admin/products/{sku}", (string sku, ShopifyProduct body) =>
{
    // The path SKU is authoritative — it is the storage key. We normalise the stored
    // record to carry it so a mismatched/absent body.sku cannot fork the entry.
    var stored = body with { Sku = sku };
    products[sku] = stored;
    return Results.Ok(stored);
});

// ====================================================================================
// List all products.
//   GET /admin/products  ->  { products: [ ...stored... ], count: <int> }
// ====================================================================================
app.MapGet("/admin/products", () =>
{
    var all = products.Values.ToList();
    return Results.Ok(new ProductListResponse(all, all.Count));
});

// ====================================================================================
// Get a single product by SKU (404 when unknown).
//   GET /admin/products/{sku}
// ====================================================================================
app.MapGet("/admin/products/{sku}", (string sku) =>
    products.TryGetValue(sku, out var product)
        ? Results.Ok(product)
        : Results.NotFound());

// Liveness probe for orchestration/CI.
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "shopify-sim" }));

app.Run();

// --- Contracts (mock-local; the sync's emitted shapes live in PartsPortal.Sync) ------

record ShopifyProductMetafields(string Unit, decimal OrderMultiple, decimal MinOrderQty, bool Backorderable);
// ListPrice is the indicative storefront list price (a catalog attribute); the contract net price
// is resolved live by the pricing service at the checkout gate, never browsed from here.
// AvailabilityBand is the synced ADVISORY band (TDD §5.2 band metafield) — display only; the live
// IVS check at the checkout gate stays authoritative (Golden Rule #5). Never a raw count (#4).
record ShopifyProduct(string Sku, string Title, string BodyHtml, string ProductType, string Status, ShopifyProductMetafields Metafields, decimal? ListPrice = null, string? AvailabilityBand = null);
record ProductListResponse(List<ShopifyProduct> Products, int Count);

namespace PartsPortal.Mocks.ShopifySim
{
    /// <summary>Public entry-point marker so WebApplicationFactory can host this app in tests.</summary>
    public sealed class ShopifySimApp;
}
