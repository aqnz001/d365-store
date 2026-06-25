# CLAUDE.md — Customer Parts Ordering Portal

Persistent context for Claude Code. Read this before doing anything. Full design lives in `docs/` (`02-Solution-Architecture-Document.md`, `03-Technical-Design-Document.md`, `01-Phased-Delivery-Plan.md`, `00-Claude-Code-Handover.md`). **Decisions taken on the owner's behalf are logged in `docs/04-Decision-Register.md` — read it for the current direction.**

## What this is
A B2B parts ordering portal: a **custom web app storefront** (React + TypeScript SPA + ASP.NET Core BFF) plus an **Azure integration layer** mediating between the storefront and **D365 FinOps** (system of record). The **Inventory Visibility Service (IVS)** is the real-time availability and reservation authority.

> **Storefront pivot (DR-001):** Shopify Plus was dropped for cost; we build a custom storefront. The Azure integration layer is storefront-agnostic (the middleware is a plain HTTP contract any client calls), so T1–T5 carry over unchanged. See the Decision Register for the storefront re-plan (tasks S1–S7) and the payments/auth/hosting choices.

**Current phase: Phase 1 — build against contracts/mocks. No live D365.** Build the interfaces for D365/IVS/pricing against mocks; they get swapped for a sandbox in Phase 2 with no code change.

## Golden rules (do not violate)
1. **No live D365 this phase.** Integrate only with mocks. Keep all external endpoints config-driven.
2. **IVS is the sole inventory-reservation authority.** Never reserve, decrement, or write inventory anywhere else.
3. **Read/write separation.** Browse/catalog reads come from BYOD / the storefront catalog only — never query FinOps/OData for browsing.
4. **Availability = ATP/AFR, never raw on-hand.** Publish `max(0, ATP − classBuffer)` as bands, never exact counts.
5. **Synced stock is advisory; the live check is authoritative.** Commit only after a live availability check + soft reservation at the checkout gate.
6. **Idempotency everywhere.** Every reservation and order carries an idempotency key; writeback de-dups before create.
7. **Queue-backed writeback.** Checkout never blocks on the ERP. Orders flow through Service Bus with retry + DLQ.
8. **Header → lines** for sales orders, linked by order number. Use Service Bus sessions for controlled concurrency — never parallel-hammer order entities.
9. **No secrets in code or config.** Key Vault + managed identity only. Never commit a credential.
10. **Contracts are provisional** (pending Phase 2 sandbox validation). Keep them centralized in `integration/contracts` — a field-name change is a one-place edit.
11. **No sensitive data or auth/session tokens in browser storage.** Keep tokens/session server-side in the BFF (DR-002/DR-004); the SPA holds only non-sensitive UI state.
12. **Surface, don't guess.** For any Open Decision (below), stop and flag it — do not invent behavior.

## Stack (confirm before deviating)
- Azure Functions / middleware: **.NET 10 (C#)**
- IaC: **Bicep**
- Storefront: **custom web app** — React + TypeScript SPA (Vite) + **ASP.NET Core BFF** (.NET 10) calling the middleware via APIM (DR-002)
- Payments: hosted provider, PCI **SAQ-A** — Stripe default behind `IPaymentProvider` (DR-003)
- Customer auth: **Entra External ID**, OIDC terminated at the BFF (DR-004)
- Mocks: lightweight HTTP simulators (ASP.NET minimal API)
- Messaging: Azure Service Bus
- Tests: unit + integration (against mocks) + contract tests

## Structure
```
docs/                 SAD, TDD, Delivery Plan, Handover, Decision Register
storefront/           custom web app (DR-001/DR-002)
  web/                React + TS SPA (Vite) — catalog, cart, checkout  (tasks S2–S5)
  bff/                ASP.NET Core backend-for-frontend — auth, middleware calls  (S1)
  extensions/         [OBSOLETE] Shopify checkout extensions — removed when S1–S3 land
integration/
  functions/          Availability, PricingCredit, Writeback, Sync, ReservationRelease
  shared/             models, mapping, idempotency, saga, http clients
  contracts/          OpenAPI + message schemas (single source of truth)
  infra/              Bicep (Service Bus, APIM, Functions, Key Vault, Redis)
mocks/                ivs-sim, odata-sim, pricing-credit-sim, shopify-sim (→ storefront catalog, DR-005)
sync/                 BYOD → storefront catalog jobs
tests/
```

## Conventions
- Every PR references its task ID (T1–T12 in the Handover) and the relevant TDD/SAD section.
- Cover negative paths: shortfall, duplicate, transient + permanent failure, credit hold.
- Endpoints, TTLs, and buffers come from configuration, not hardcoded literals.
- Correlation ID propagated cart → reserve → order → fulfilment.

## Commands
Stack: **.NET 10 (C#) + Bicep**. Solution file is **`PartsPortal.slnx`** (the .NET 10 XML format). SDK pinned in `global.json`; NuGet versions in `Directory.Packages.props` (Central Package Management — `PackageReference` carries no `Version`).
- Build: `dotnet build PartsPortal.slnx -c Release`
- Test: `dotnet test PartsPortal.slnx`
- Lint: `dotnet format PartsPortal.slnx whitespace --verify-no-changes && dotnet format PartsPortal.slnx style --verify-no-changes` (apply: `dotnet format PartsPortal.slnx`)
- Run mocks: `dotnet run --project mocks/ivs-sim` (5101) · `mocks/odata-sim` (5102) · `mocks/pricing-credit-sim` (5103)
- Run a function: `cd integration/functions/<Name> && func start` (needs Azure Functions Core Tools v4)
- IaC validate: `az bicep build --file integration/infra/main.bicep` (or `bicep build ...`)
- Generate contract models: `dotnet run --project tools/contract-gen` (regenerate `integration/shared/Generated/*.g.cs` after any change to `integration/contracts`; never hand-edit generated `*.g.cs`)

## Do NOT
- Call real D365/IVS/pricing endpoints this phase.
- Reserve/write inventory outside the IVS interface.
- Read FinOps/OData for browse.
- Publish raw on-hand or exact stock counts.
- Put secrets in code/config, or personal data in URLs/logs.
- Put auth/session tokens or sensitive data in browser storage (keep them in the BFF).
- Parallel-hammer order entities.
- Silently resolve an Open Decision — log it in `docs/04-Decision-Register.md` instead.

## Open decisions (surface via `docs/04-Decision-Register.md`; don't guess silently)
During autonomous build, decisions made on the owner's behalf go in the **Decision Register** (don't block). Status:
1. ✓ Resolved — .NET 10 + Bicep (DR-000a).
2. ✓ Resolved — **custom web app, no Shopify** (DR-001); storefront stack/payments/auth in DR-002–DR-006.
3. Sales-order number ownership (FinOps-generated assumed) — Open.
4. Reservation TTL value + release triggers — Open (config-driven placeholder).
5. Initial per-class buffer values + band thresholds — Open (config-driven placeholder).
6. Recurring orders: custom subscription scheduler — **deferred** (DR-007).
7. Multi-warehouse: aggregate vs branch-specific availability — Open.
8. Connector boundary — keep custom flows connector-agnostic regardless.

## First session
1. Read SAD + TDD + Delivery Plan.
2. Execute **T1** (scaffold, this `CLAUDE.md`, CI, IaC skeleton) → PR.
3. Then **T2** (contracts) and **T3** (mocks) — they unblock everything else.
4. Flag any Open Decision needed before proceeding past T4.
