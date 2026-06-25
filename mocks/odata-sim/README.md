# odata-sim — FinOps OData writeback simulator

Phase 1 mock standing in for **D365 FinOps OData** sales-order writeback (TDD §4.4).
Deterministic, seedable, and able to inject transient failures to exercise retry/DLQ
paths (Handover §8). Replaced by the FinOps sandbox in Phase 2 — entity names/shapes
mirror the contracts.

Enforces **header → lines** ordering: a line is rejected unless its parent header
exists, and lines referencing unknown master data are rejected with HTTP 400.

> **Open Decision #3 — sales-order number ownership.** This mock ASSUMES FinOps owns
> the number sequence and generates the order number on header create. If the decision
> lands elsewhere, only the generator in `Program.cs` changes.

## Run

```bash
dotnet run --project mocks/odata-sim
```

Listens on **http://localhost:5102** (single `http` profile).

## Endpoints

| Method | Path | Purpose (TDD §4.4) |
| ------ | ---- | ------------------ |
| POST | `/data/SalesOrderHeadersV2` | Create a header; generate + return a FinOps-owned sales order number. Unknown customer → HTTP 400. |
| POST | `/data/SalesOrderLines` | Create a line. Requires a valid parent sales order number (FK); unknown item or missing header → HTTP 400. |
| POST | `/admin/seed` | Register known items + customers for validation. |
| GET  | `/health` | Liveness probe. |

### Seed example

```bash
curl -X POST http://localhost:5102/admin/seed \
  -H 'content-type: application/json' \
  -d '{ "items": ["ITEM-1","ITEM-2"], "customers": ["CUST-1"] }'
```

### Header then line example

```bash
# 1. Create header -> returns salesOrderNumber
curl -X POST http://localhost:5102/data/SalesOrderHeadersV2 \
  -H 'content-type: application/json' \
  -d '{ "customerAccount": "CUST-1" }'

# 2. Create a line against that order number
curl -X POST http://localhost:5102/data/SalesOrderLines \
  -H 'content-type: application/json' \
  -d '{ "salesOrderNumber": "SO-000001", "itemNumber": "ITEM-1", "quantity": 3 }'
```

## Toggling transient failure

* **Config (global):** set `Simulate:TransientFailureRate` in `appsettings.json` to a
  probability between `0.0` (never) and `1.0` (always). Failures return HTTP 503.
* **Per request (force):** send header `x-sim-fail: transient` to force a 503 on that
  call — useful for deterministically exercising the retry/DLQ path.
