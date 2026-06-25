# pricing-credit-sim — Pricing + credit resolution simulator

Phase 1 mock standing in for the **PortalPricing** service (TDD §4.5). Resolves a
deterministic net effective price per line and reports the customer's credit status.
Seedable and configurable so tests are repeatable and the over-limit / hold negative
paths (Handover §8) can be exercised. Replaced by the real pricing/credit service in
Phase 2 — request/response shapes mirror the contracts.

## Run

```bash
dotnet run --project mocks/pricing-credit-sim
```

Listens on **http://localhost:5103** (single `http` profile).

## Endpoints

| Method | Path | Purpose (TDD §4.5) |
| ------ | ---- | ------------------ |
| POST | `/api/services/PortalPricing/resolve` | Deterministic net effective price per line + credit status. |
| POST | `/admin/seed` | Set per-item unit prices and per-customer credit status. |
| GET  | `/health` | Liveness probe. |

### Seed example

```bash
curl -X POST http://localhost:5103/admin/seed \
  -H 'content-type: application/json' \
  -d '{ "prices": [ { "itemNumber": "ITEM-1", "unitPrice": 12.50 } ], "credit": [ { "customerAccount": "CUST-1", "status": "OK" } ] }'
```

### Resolve example

```bash
curl -X POST http://localhost:5103/api/services/PortalPricing/resolve \
  -H 'content-type: application/json' \
  -d '{ "customerAccount": "CUST-1", "lines": [ { "itemNumber": "ITEM-1", "quantity": 4 } ] }'
```

## Toggling credit status

Credit verdict is one of `OK`, `over-limit`, `hold`. Precedence (highest first):

1. **Per request:** header `x-sim-credit: over-limit` (or `hold` / `OK`).
2. **Per customer:** seed via `/admin/seed` (`credit[].status`).
3. **Config (global default):** `Credit:Status` in `appsettings.json`.

Unseeded items resolve at a `0.00` unit price so callers can detect a missing price.
