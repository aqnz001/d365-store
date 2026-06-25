# Claude Code Handover — Customer Parts Ordering Portal (Phase 1: Build Against Contracts/Mocks)

| | |
|---|---|
| **Status** | Ready for agent execution |
| **Phase** | Phase 1 — buildable now, **no live D365 required** |
| **Read first** | `02-Solution-Architecture-Document.md`, `03-Technical-Design-Document.md`, `01-Phased-Delivery-Plan.md` |
| **Owner** | _[Solution Architect]_ |

---

## 0. How to use this document

This is a handover for **Claude Code**. Before starting, read the SAD (the *why*), the TDD (the *how* — contracts, mappings, sequences), and the Delivery Plan (scope/sequencing). This doc tells you **what to build in Phase 1, the order, and what "done" means**. It deliberately scopes to work that needs **no live D365** — everything here builds against documented contracts and mocks.

**First action:** create a `CLAUDE.md` at the repo root seeded from §3 (Guardrails) and §4 (Stack & structure) so the rules persist across sessions.

---

## 1. Mission

Build a B2B parts ordering portal: a **Shopify Plus** storefront and an **Azure integration layer** that mediates between Shopify and **D365 FinOps**. FinOps is the system of record; the **Inventory Visibility Service (IVS)** is the real-time availability and reservation authority. In this phase you build the storefront, the full integration scaffolding and logic, the catalog sync from the existing BYOD, and **mock services** standing in for IVS / OData / pricing-credit so the whole system runs end-to-end without a live ERP.

---

## 2. In scope (Phase 1) / out of scope

**In scope — build now:**
- Shopify storefront, B2B config, customer accounts, checkout-validation extensions.
- Azure integration: API Management config, Functions/Logic Apps, Service Bus topology, Key Vault wiring, optional Redis.
- Middleware logic: availability check, soft reservation, order writeback, **idempotency + saga/compensation**, retry/DLQ.
- Catalog sync from the **existing BYOD** (real data) → Shopify.
- **Mock/simulator services** for IVS, FinOps OData, pricing/credit.
- Contracts as code (OpenAPI + message schemas), IaC, CI/CD, observability scaffolding, tests.

**Out of scope this phase (needs sandbox/production — do NOT attempt):**
- Real IVS configuration/behavior, real reservation conversion, real OData writeback validation, live pricing/credit resolution, performance/throttling tests, UAT, cutover. Build the *interfaces* for these against mocks; validation happens in Phase 2.

---

## 3. Guardrails (seed `CLAUDE.md` with these)

1. **No live D365 in this phase.** Integrate only with mocks/simulators. Make endpoints configurable so Phase 2 can swap mocks for sandbox URLs without code changes.
2. **IVS is the single inventory-reservation authority.** No other component writes/reserves inventory. Never reserve or decrement stock directly.
3. **Read/write separation.** Browse/catalog reads come from BYOD/Shopify only — never call FinOps/OData for browsing.
4. **Availability uses ATP/AFR, never raw on-hand.** Publish `max(0, ATP − classBuffer)` as bands, not exact counts.
5. **The synced stock number is advisory; the live check is authoritative.** Commit only after a live availability check + soft reservation at the checkout gate.
6. **Idempotency everywhere.** Every reservation and order carries an idempotency key; writeback de-dups before create.
7. **Queue-backed writeback.** Checkout never blocks on the ERP; orders flow through Service Bus with retry/DLQ.
8. **Header → lines** for sales orders, linked by order number; controlled concurrency (Service Bus sessions) — never parallel-hammer order entities.
9. **No secrets in code or config.** Key Vault + managed identity only. No credentials in the repo, ever.
10. **Contracts are provisional.** TDD payloads follow Microsoft's documented shapes but are **to be validated in Phase 2**. Centralize them (§7) so a field-name change is a one-place edit.
11. **No browser storage** (localStorage/sessionStorage) in any storefront extension — use platform-supported state only.
12. **Surface, don't guess.** For any item in §10 (Open decisions), stop and flag it rather than inventing a behavior.

---

## 4. Proposed stack & repo structure

Defaults below are recommendations — confirm with the owner before deviating. Chosen to fit an Azure/D365 enterprise shop.

- **Azure Functions / middleware:** .NET (C#) _(alt: TypeScript)_
- **IaC:** Bicep _(alt: Terraform)_
- **Storefront:** Shopify theme (Liquid) + checkout UI extensions (JS/React) + Shopify Functions for validation
- **Mocks:** lightweight HTTP simulators (ASP.NET minimal API or WireMock)
- **Messaging:** Azure Service Bus
- **Tests:** unit + integration (against mocks) + contract tests

```
/
├─ CLAUDE.md                      # seeded from §3 + §4
├─ docs/                          # SAD, TDD, Delivery Plan, this handover
├─ storefront/                    # Shopify theme + extensions
│  ├─ extensions/checkout-validate/
│  └─ extensions/availability-display/
├─ integration/
│  ├─ functions/
│  │  ├─ Availability/            # /cart/validate, /cart/reserve, /cart/release
│  │  ├─ PricingCredit/           # resolve price + credit (calls mock)
│  │  ├─ Writeback/               # queue-triggered order → FinOps (mock)
│  │  ├─ Sync/                    # catalog/inventory/price/customer/status
│  │  └─ ReservationRelease/      # TTL expiry/release
│  ├─ shared/                     # models, mapping, idempotency, saga, http clients
│  ├─ contracts/                  # OpenAPI + message schemas (single source)
│  └─ infra/                      # Bicep (Service Bus, APIM, Functions, KV, Redis)
├─ mocks/
│  ├─ ivs-sim/                    # query ATP, reserve, release, allocation
│  ├─ odata-sim/                  # sales order header/lines
│  └─ pricing-credit-sim/
├─ sync/                          # BYOD → Shopify catalog jobs (may live under functions)
└─ tests/
```

---

## 5. Work breakdown (ordered, agent-sized tasks)

Each task: build, test against mocks, meet acceptance criteria, open a PR. Tasks are ordered by dependency; later tasks assume earlier ones.

### T1 — Repo, CLAUDE.md, CI/CD, IaC skeleton
- Scaffold the structure (§4); seed `CLAUDE.md`; CI (build, lint, test) and Bicep skeleton for Service Bus, APIM, Functions, Key Vault, Redis.
- **Done:** repo builds in CI; IaC validates (what-if/plan); no secrets present.

### T2 — Contracts as code
- Author OpenAPI for the middleware surface (TDD §4.6) and JSON schemas for the order message and status event (TDD §4.4, §5.5). Generate models from contracts.
- **Done:** contracts validate; models generated; one source of truth in `integration/contracts`.

### T3 — Mock services
- `ivs-sim` (ATP query, reserve→reservation ID, release, allocation), `odata-sim` (header→lines, returns order number, validates master-data presence), `pricing-credit-sim` (effective price + credit status). Deterministic, seedable, configurable to simulate shortfalls/failures.
- **Done:** mocks runnable locally + in CI; can be pointed at via config; can be told to return shortfall/failure for negative tests.

### T4 — Shared libraries
- Models, field-mapping helpers (TDD §5), idempotency store abstraction, saga/compensation helper, resilient HTTP clients with backoff.
- **Done:** unit-tested; idempotency and backoff covered.

### T5 — Catalog sync (BYOD → Shopify)
- Read the **existing BYOD** catalog; map (TDD §5.1) and sync to Shopify (or a Shopify mock if no dev store yet); include min-qty/order-multiple/UoM/backorderable/lifecycle metafields.
- **Done:** catalog populates from BYOD sample; delisting on lifecycle change works; idempotent re-runs.

### T6 — Availability function (`/cart/validate`, `/cart/reserve`, `/cart/release`)
- Live ATP check via `ivs-sim`; band logic + buffer formula (TDD §7.2); soft reservation returning IDs; release endpoint.
- **Done:** validate returns correct band/decision; reserve returns IDs and decrements sim AFR; shortfall path returns options; release frees AFR.

### T7 — Pricing/credit function
- Resolve effective price + credit status via `pricing-credit-sim`; lock prices onto the cart; credit-hold path.
- **Done:** prices locked; over-limit/hold routes to block/approval path.

### T8 — Checkout-validation extension (storefront)
- Shopify checkout extension calling `/cart/validate` + `/cart/reserve` at the **checkout gate**; block commit on shortfall/credit; surface reduce/backorder/split options. Availability-display extension shows bands.
- **Done:** checkout blocks when sim returns shortfall; reservation placed before payment; no browser storage used.

### T9 — Order writeback (queue-triggered)
- Service Bus `orders-inbound` (sessions per customer); idempotency check; create header→lines via `odata-sim`; convert reservation in `ivs-sim`; DLQ + saga/compensation on failure (release reservation, mark for CSR).
- **Done:** happy path creates order + converts reservation; duplicate message returns existing order (no re-create); permanent failure compensates + dead-letters; transient retries with backoff.

### T10 — Status / fulfilment sync
- Consume mock business events (pack/ship/invoice) via `status-outbound`; map to Shopify fulfilments incl. **partial/multiple shipments**.
- **Done:** single order → multiple fulfilments with tracking; remaining backorder reflected.

### T11 — Scenario coverage (against mocks)
- Implement/test: backorder, advance/block (allocation), recurring (re-check + re-resolve each run), partial fulfilment, credit hold, price integrity (locked-price tolerance), cancellation (release), returns, min-qty/UoM, kits, made-to-order, multi-warehouse. (TDD §6, SAD §8.)
- **Done:** each scenario has a passing integration test against mocks; reservation state machine (TDD §7.1) honored.

### T12 — Observability + reservation-release job
- Correlation IDs cart→reserve→order→fulfilment; metrics for oversell rate, reservation leak, DLQ depth, sync lag; TTL release job for stale reservations.
- **Done:** correlation traceable end-to-end; stale reservations auto-release; metrics emitted.

---

## 6. Definition of done (per task & phase)

- Code + tests; CI green; PR with summary referencing the task ID and relevant TDD/SAD section.
- Negative paths covered (shortfall, duplicate, transient + permanent failure, credit hold).
- No secrets; endpoints config-driven; contracts centralized.
- **Phase exit:** full happy path runs end-to-end against mocks (browse → validate → reserve → pay → writeback → status), and all §5 scenarios pass against mocks. Ready to swap mocks for a Tier-1 sandbox in Phase 2.

---

## 7. Contracts (build against these — see TDD §4 for detail)

- **Middleware (Shopify-facing):** `/cart/validate`, `/cart/reserve`, `/cart/release`, `/order`, `/order/{id}/status`.
- **IVS (mock now):** ATP query (`onhand/indexquery`, `QueryATP=true`), `onhand/reserve` (returns reservation ID, `ifCheckAvailForReserv`), release, allocation.
- **FinOps OData (mock now):** `SalesOrderHeadersV2` then `SalesOrderLines` (FK = order number); mock enforces master-data presence.
- **Pricing/credit (mock now):** effective price per line + credit status.

Keep all of these in `integration/contracts` as the single source of truth. Treat field names as provisional pending Phase 2 sandbox validation.

---

## 8. Mocks — required behaviors

- **ivs-sim:** maintain in-memory AFR; reserve decrements and returns an ID; release restores; allocation ring-fences a pool; ATP query supports forward-dating flag; can be configured to return shortfall.
- **odata-sim:** create header → return order number; create lines linked by number; **reject** lines referencing missing master data (to exercise the validation path); support an injected transient-failure mode.
- **pricing-credit-sim:** return deterministic effective prices and a configurable credit status (OK / over-limit / hold).

---

## 9. What NOT to do

- Don't call any real D365/IVS/pricing endpoint in this phase.
- Don't reserve or write inventory anywhere except via the IVS interface.
- Don't read FinOps/OData for browse/catalog.
- Don't publish raw on-hand or exact counts.
- Don't put secrets in code/config or personal data in URLs/logs.
- Don't use browser storage in storefront extensions.
- Don't parallel-hammer the order entities — use sessions/controlled concurrency.
- Don't silently resolve an Open Decision (§10) — flag it.

---

## 10. Open decisions — surface to the owner, do not guess

1. Stack confirmation (.NET vs TypeScript; Bicep vs Terraform).
2. Is there a Shopify dev store yet, or sync to a Shopify mock for now?
3. Sales-order number ownership (FinOps-generated assumed).
4. Reservation TTL value + release triggers.
5. Initial per-class buffer values and band thresholds.
6. Recurring orders: Shopify subscription vs FinOps blanket agreement (affects T11).
7. Multi-warehouse: aggregate vs branch-specific availability.
8. Connector boundary (which sync flows a packaged connector owns vs custom) — even if deferred, keep custom flows connector-agnostic.

---

## 11. Suggested first session

1. Read SAD + TDD + Delivery Plan.
2. Execute **T1** (scaffold, `CLAUDE.md`, CI, IaC skeleton) and open a PR.
3. Then **T2** (contracts) and **T3** (mocks) — these unblock everything else.
4. Flag any §10 decision needed before proceeding past T4.
