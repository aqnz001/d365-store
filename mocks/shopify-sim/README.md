# shopify-sim — Shopify catalog simulator

Phase 1 mock standing in for the **Shopify Plus** storefront's Admin product surface.
It is the write target the catalog sync (T5, BYOD → Shopify, TDD §5.1) upserts into.
In-memory and idempotent, so repeated sync runs converge to the same state. Replaced
by a real Shopify dev store in Phase 2 with no caller code change (Open Decision #2) —
endpoint shapes mirror what the sync emits.

> Products are keyed by **SKU**. `status` is `active` (live) or `archived` (delisted,
> e.g. Discontinued). The lifecycle → status decision lives in the sync; this mock
> stores whatever it receives.

## Run

```bash
dotnet run --project mocks/shopify-sim
```

Listens on **http://localhost:5104** (single `http` profile).

## Endpoints

| Method | Path | Purpose |
| ------ | ---- | ------- |
| PUT  | `/admin/products/{sku}` | Upsert by SKU. Store/overwrite and echo the stored product (idempotent). |
| GET  | `/admin/products` | List all products — `{ products: [...], count: <int> }`. |
| GET  | `/admin/products/{sku}` | The single product, or 404 when unknown. |
| GET  | `/health` | Liveness probe. |

### Upsert example

```bash
curl -X PUT http://localhost:5104/admin/products/PART-001 \
  -H 'content-type: application/json' \
  -d '{
        "sku": "PART-001",
        "title": "Brake Pad Set",
        "bodyHtml": "<p>Front axle brake pads.</p>",
        "productType": "Brakes",
        "status": "active",
        "metafields": {
          "unit": "EA",
          "orderMultiple": 1,
          "minOrderQty": 1,
          "backorderable": true
        }
      }'
```

### List example

```bash
curl http://localhost:5104/admin/products
```
