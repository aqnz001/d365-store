# integration/contracts — Contracts as code (single source of truth)

This directory is the **centralized single source of truth** for the integration
contracts between Shopify, the Azure middleware, D365 FinOps, the Inventory
Visibility Service (IVS), and the pricing/credit service (Golden Rule #10).

A field-name or shape change is a **one-place edit** here. Do not redefine these
shapes ad hoc in function or storefront code — generate from these contracts
instead.

## Status: provisional

All contracts here are **PROVISIONAL, pending Phase 2 sandbox validation.**
Phase 1 builds against mocks; there is **no live D365** this phase. Field names,
enums, TTLs, and bands may change once validated against the D365/IVS sandbox.
When that happens, edit the contract here first, then regenerate.

## Contents

| File | Purpose | TDD ref |
| --- | --- | --- |
| `openapi/middleware.yaml` | OpenAPI 3.0.3 for the Shopify-facing middleware: `/cart/validate`, `/cart/reserve`, `/cart/release`, `/order`, `/order/{id}/status`. OAuth2 client-credentials (Entra). | §4.6 |
| `openapi/ivs.yaml` | OpenAPI 3.0.3 for the Inventory Visibility Service: `indexquery` (ATP/AFR), `reserve`, `release`, `allocation`. Mirrors `mocks/ivs-sim`. | §4.1–4.3 |
| `openapi/odata-salesorder.yaml` | OpenAPI 3.0.3 for FinOps OData writeback: `SalesOrderHeadersV2` then `SalesOrderLines` (header→lines, master-data validation). Mirrors `mocks/odata-sim`. | §4.4 |
| `openapi/pricing-credit.yaml` | OpenAPI 3.0.3 for the pricing/credit service: `PortalPricing/resolve` (effective price + credit status). Mirrors `mocks/pricing-credit-sim`. | §4.5 |
| `schemas/order-message.schema.json` | JSON Schema (draft 2020-12) for the Service Bus `orders-inbound` message envelope. | §4.4 / §5.5 / §8 |
| `schemas/status-event.schema.json` | JSON Schema (draft 2020-12) for FinOps -> Shopify status / fulfilment events. | §6.3 |

## Models are generated from these

C# DTOs are generated from every contract here into
`integration/shared/Generated/*.g.cs` (namespaces `PartsPortal.Shared.Contracts.*`)
by the dev tool [`tools/contract-gen`](../../tools/contract-gen). Treat the generated
`*.g.cs` as **derived artifacts** — change the contract, regenerate, never hand-edit.

```
dotnet run --project tools/contract-gen      # regenerate after any contract change
```

| Contract | Generated namespace |
| --- | --- |
| `openapi/middleware.yaml` | `PartsPortal.Shared.Contracts.Middleware` |
| `openapi/ivs.yaml` | `PartsPortal.Shared.Contracts.Ivs` |
| `openapi/odata-salesorder.yaml` | `PartsPortal.Shared.Contracts.OdataSalesorder` |
| `openapi/pricing-credit.yaml` | `PartsPortal.Shared.Contracts.PricingCredit` |
| `schemas/*.schema.json` | `PartsPortal.Shared.Contracts.Messages` |

CI fails if the committed `Generated/` output drifts from the contracts (the
generator is re-run and the tree must be clean). The generator also doubles as the
**contract validation** gate — it parses every contract, so a malformed OpenAPI or
JSON Schema fails generation.

## Conventions reflected here

- **IVS is the sole inventory-reservation authority.** Reserve/release flow only
  through `/cart/reserve` and `/cart/release`; clients never reserve or write
  inventory directly.
- **Availability is published as bands**, never raw on-hand or exact counts
  (`max(0, ATP - classBuffer)`); band thresholds are config-driven.
- **Idempotency everywhere** — orders carry an `idempotencyKey`; writeback de-dups
  before any FinOps create.
- **Queue-backed writeback** — checkout enqueues `orders-inbound`; Service Bus
  sessions give controlled per-(customer/site) concurrency (never parallel-hammer
  order entities).
- **Correlation ID** is propagated cart -> reserve -> order -> fulfilment.
- **No secrets** in any contract — OAuth token URLs/scopes are placeholders;
  client secrets are provisioned via Key Vault + managed identity only.

## Upstream contracts — captured (T2)

Every external boundary now has a contract here. Each mirrors its Phase-1 mock
exactly (contract ≡ mock), so swapping mocks for the sandbox in Phase 2 is a
config change, not a contract change:

- [x] **IVS** — `openapi/ivs.yaml`: `indexquery` (ATP/AFR), `reserve`, `release`,
      `allocation` (the reservation + ATP/AFR authority).
- [x] **D365 OData** — `openapi/odata-salesorder.yaml`: sales-order **header** then
      **lines** (linked by order number; master-data validation path).
- [x] **Pricing / credit** — `openapi/pricing-credit.yaml`: `PortalPricing/resolve`
      (effective price + credit status driving the credit-hold decision).

## Open decisions surfaced (do not guess — see root `CLAUDE.md`)

- #3 Sales-order number ownership (FinOps-generated assumed; `salesOrderNumber`
  is null until written back).
- #4 Reservation TTL value + release triggers (`ttlSeconds` is config-driven).
- #5 Per-class buffer values + band thresholds (drive `AvailabilityBand`).
- #7 Multi-warehouse: aggregate vs branch-specific availability (affects `site`).
