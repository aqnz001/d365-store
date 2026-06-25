# ivs-sim — Inventory Visibility Service simulator

Phase 1 mock standing in for the real **IVS** (the sole authority for inventory
availability and reservation). Deterministic, seedable, and able to inject shortfall
to exercise negative paths (Handover §8). Replaced by the real IVS sandbox in Phase 2
with no caller code change — endpoint shapes mirror TDD §4.1–4.3.

> Tracks **ATP / AFR** only. Raw on-hand is never stored or returned.

## Run

```bash
dotnet run --project mocks/ivs-sim
```

Listens on **http://localhost:5101** (single `http` profile).

## Endpoints

| Method | Path | Purpose (TDD) |
| ------ | ---- | ------------- |
| POST | `/api/environment/{environmentId}/onhand/indexquery?QueryATP=true` | §4.1 ATP/AFR per product/dimension. Body `returnNegative` (default false) clamps negatives to 0. |
| POST | `/api/environment/{environmentId}/onhand/reserve` | §4.2 With `ifCheckAvailForReserv`, validate AFR; on success decrement AFR + return a reservation id; on shortfall return a documented shortfall response (HTTP 409). |
| POST | `/api/environment/{environmentId}/onhand/release` | §4.3 Restore AFR for a reservation id (idempotent on unknown ids). |
| POST | `/api/environment/{environmentId}/allocation` | Ring-fence a pool — reduces AFR/ATP. |
| POST | `/admin/seed` | Set AFR/ATP for items (deterministic test setup). |
| GET  | `/health` | Liveness probe. |

### Seed example

```bash
curl -X POST http://localhost:5101/admin/seed \
  -H 'content-type: application/json' \
  -d '{ "items": [ { "productId": "PART-001", "site": "S1", "location": "L1", "afr": 25, "atp": 25 } ] }'
```

### Reserve example

```bash
curl -X POST http://localhost:5101/api/environment/dev/onhand/reserve \
  -H 'content-type: application/json' \
  -d '{ "productId": "PART-001", "site": "S1", "location": "L1", "quantity": 5, "ifCheckAvailForReserv": true }'
```

## Toggling shortfall

* **Config (global):** set `Simulate:Shortfall` to `true` in `appsettings.json`.
* **Per request (override):** send header `x-sim-shortfall: true` (or `false`).

When shortfall is active, every `reserve` returns the documented shortfall response
(HTTP 409, `availableQuantity: 0`) regardless of seeded AFR.
