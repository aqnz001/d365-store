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
| `schemas/order-message.schema.json` | JSON Schema (draft 2020-12) for the Service Bus `orders-inbound` message envelope. | §4.4 / §5.5 / §8 |
| `schemas/status-event.schema.json` | JSON Schema (draft 2020-12) for FinOps -> Shopify status / fulfilment events. | §6.3 |

## Models are generated from these (T2)

The shared models in `integration/shared` (assembly `PartsPortal.Shared`) are
**generated from these contracts** as part of task **T2**. Treat the generated
types as derived artifacts: change the contract, regenerate, never hand-edit the
generated output. TODO(T2): wire up the generation step and document the command
in the root `CLAUDE.md` Commands section.

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

## Upstream contracts still to add — TODO(T2)

These are referenced by the design but **not yet captured here**. Add them in T2
so every external boundary has a contract in this directory:

- [ ] **IVS** — `TODO(T2)` index/query (availability), reserve, and allocation
      message/operation contracts (the reservation + ATP/AFR authority).
- [ ] **D365 OData** — `TODO(T2)` sales-order **header** and **lines** entity
      contracts for writeback (header -> lines linked by order number).
- [ ] **Pricing / credit** — `TODO(T2)` price lookup and credit-check / credit-hold
      contract used to produce locked prices and the credit-hold decision.

## Open decisions surfaced (do not guess — see root `CLAUDE.md`)

- #3 Sales-order number ownership (FinOps-generated assumed; `salesOrderNumber`
  is null until written back).
- #4 Reservation TTL value + release triggers (`ttlSeconds` is config-driven).
- #5 Per-class buffer values + band thresholds (drive `AvailabilityBand`).
- #7 Multi-warehouse: aggregate vs branch-specific availability (affects `site`).
