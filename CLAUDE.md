# CLAUDE.md — Customer Parts Ordering Portal

Persistent context for Claude Code. Read this before doing anything. Full design lives in `docs/` (`02-Solution-Architecture-Document.md`, `03-Technical-Design-Document.md`, `01-Phased-Delivery-Plan.md`, `00-Claude-Code-Handover.md`).

## What this is
A B2B parts ordering portal: a **Shopify Plus** storefront plus an **Azure integration layer** mediating between Shopify and **D365 FinOps** (system of record). The **Inventory Visibility Service (IVS)** is the real-time availability and reservation authority.

**Current phase: Phase 1 — build against contracts/mocks. No live D365.** Build the interfaces for D365/IVS/pricing against mocks; they get swapped for a sandbox in Phase 2 with no code change.

## Golden rules (do not violate)
1. **No live D365 this phase.** Integrate only with mocks. Keep all external endpoints config-driven.
2. **IVS is the sole inventory-reservation authority.** Never reserve, decrement, or write inventory anywhere else.
3. **Read/write separation.** Browse/catalog reads come from BYOD/Shopify only — never query FinOps/OData for browsing.
4. **Availability = ATP/AFR, never raw on-hand.** Publish `max(0, ATP − classBuffer)` as bands, never exact counts.
5. **Synced stock is advisory; the live check is authoritative.** Commit only after a live availability check + soft reservation at the checkout gate.
6. **Idempotency everywhere.** Every reservation and order carries an idempotency key; writeback de-dups before create.
7. **Queue-backed writeback.** Checkout never blocks on the ERP. Orders flow through Service Bus with retry + DLQ.
8. **Header → lines** for sales orders, linked by order number. Use Service Bus sessions for controlled concurrency — never parallel-hammer order entities.
9. **No secrets in code or config.** Key Vault + managed identity only. Never commit a credential.
10. **Contracts are provisional** (pending Phase 2 sandbox validation). Keep them centralized in `integration/contracts` — a field-name change is a one-place edit.
11. **No browser storage** (localStorage/sessionStorage) in storefront extensions. Use platform-supported state only.
12. **Surface, don't guess.** For any Open Decision (below), stop and flag it — do not invent behavior.

## Stack (confirm before deviating)
- Azure Functions / middleware: **.NET (C#)** _(alt: TypeScript)_
- IaC: **Bicep** _(alt: Terraform)_
- Storefront: Shopify theme (Liquid) + checkout UI extensions (JS/React) + Shopify Functions
- Mocks: lightweight HTTP simulators (ASP.NET minimal API or WireMock)
- Messaging: Azure Service Bus
- Tests: unit + integration (against mocks) + contract tests

## Structure
```
docs/                 SAD, TDD, Delivery Plan, Handover
storefront/           Shopify theme + extensions (checkout-validate, availability-display)
integration/
  functions/          Availability, PricingCredit, Writeback, Sync, ReservationRelease
  shared/             models, mapping, idempotency, saga, http clients
  contracts/          OpenAPI + message schemas (single source of truth)
  infra/              Bicep (Service Bus, APIM, Functions, Key Vault, Redis)
mocks/                ivs-sim, odata-sim, pricing-credit-sim
sync/                 BYOD → Shopify catalog jobs
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

## Do NOT
- Call real D365/IVS/pricing endpoints this phase.
- Reserve/write inventory outside the IVS interface.
- Read FinOps/OData for browse.
- Publish raw on-hand or exact stock counts.
- Put secrets in code/config, or personal data in URLs/logs.
- Use browser storage in extensions.
- Parallel-hammer order entities.
- Silently resolve an Open Decision.

## Open decisions (surface to the owner; don't guess)
1. Stack confirmation (.NET vs TS; Bicep vs Terraform).
2. Shopify dev store available, or sync to a Shopify mock for now?
3. Sales-order number ownership (FinOps-generated assumed).
4. Reservation TTL value + release triggers.
5. Initial per-class buffer values + band thresholds.
6. Recurring orders: Shopify subscription vs FinOps blanket agreement.
7. Multi-warehouse: aggregate vs branch-specific availability.
8. Connector boundary — keep custom flows connector-agnostic regardless.

## First session
1. Read SAD + TDD + Delivery Plan.
2. Execute **T1** (scaffold, this `CLAUDE.md`, CI, IaC skeleton) → PR.
3. Then **T2** (contracts) and **T3** (mocks) — they unblock everything else.
4. Flag any Open Decision needed before proceeding past T4.
