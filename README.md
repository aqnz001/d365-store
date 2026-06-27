# Customer Parts Ordering Portal

A B2B parts-ordering portal: a **Shopify Plus** storefront plus an **Azure integration layer**
mediating between Shopify and **D365 FinOps** (system of record), with the **Inventory
Visibility Service (IVS)** as the real-time availability and reservation authority.

> **Phase 1 — build against contracts/mocks. No live D365.** All external endpoints are
> config-driven so Phase 2 swaps mocks for a Tier-1 sandbox with no code change. Read
> [`CLAUDE.md`](CLAUDE.md) for the golden rules and [`docs/`](docs) for the full design
> (SAD, TDD, Delivery Plan, Handover, IVS Runbook).

## Repository layout

```
docs/                        SAD, TDD, Delivery Plan, Handover, IVS Config Runbook
storefront/                  Shopify theme + checkout UI extensions (T8)
  extensions/
    checkout-validate/       calls /cart/validate + /cart/reserve at the checkout gate
    availability-display/     shows availability bands (never raw counts)
integration/
  functions/                 Azure Functions (.NET 10 isolated worker)
    Availability/            /cart/validate, /cart/reserve, /cart/release  (T6)
    PricingCredit/           /pricing/resolve — effective price + credit    (T7)
    Writeback/               queue-triggered order writeback                (T9)
    Sync/                    catalog/inventory/price/customer/status sync    (T5/T10)
    ReservationRelease/      TTL release of stale soft reservations          (T12)
  shared/                    models, mapping, idempotency, saga, resilient HTTP (T4)
  contracts/                 OpenAPI + JSON schemas — single source of truth (T2)
  infra/                     Bicep: Service Bus, APIM, Functions, Key Vault, Redis
mocks/                       ivs-sim, odata-sim, pricing-credit-sim          (T3)
sync/                        BYOD → Shopify catalog sync jobs                (T5)
tests/                       unit + integration (against mocks)
```

Task IDs (T1–T12) map to the work breakdown in [`docs/00-Claude-Code-Handover.md`](docs/00-Claude-Code-Handover.md).

## Prerequisites

- **.NET SDK 10** (pinned in [`global.json`](global.json))
- **Azure Functions Core Tools v4** (`func`) — to run the Functions locally
- **Bicep CLI** or **Azure CLI** (`az bicep`) — to compile/validate IaC
- **Node 20+** — for the Shopify storefront extensions (T8)

## Common commands

| Task | Command |
|---|---|
| Restore | `dotnet restore PartsPortal.slnx` |
| Build | `dotnet build PartsPortal.slnx -c Release` |
| Test | `dotnet test PartsPortal.slnx` |
| Lint (format check) | `dotnet format PartsPortal.slnx whitespace --verify-no-changes && dotnet format PartsPortal.slnx style --verify-no-changes` |
| Format (apply) | `dotnet format PartsPortal.slnx` |
| Run a function locally | `cd integration/functions/Availability && func start` |
| Run a mock | `dotnet run --project mocks/ivs-sim` (IVS:5101, OData:5102, Pricing:5103) |
| Validate Bicep | `az bicep build --file integration/infra/main.bicep` |

Local settings: copy each function's `local.settings.json.example` to `local.settings.json`
(git-ignored) and point the `ExternalEndpoints__*` values at the local mocks. **No secrets in
the repo** — real secrets come from Key Vault via managed identity (Golden Rule #9).

### Run the whole stack locally
`./scripts/run-local.sh` starts the four mocks, the dev-gateway (re-hosts the middleware
services so you don't need Azure Functions Core Tools or a Service Bus emulator), and the BFF,
then seeds sample data. In another terminal run the SPA — `cd storefront/web && npm run dev` —
and browse → cart → checkout → pay → order all work end-to-end against the mocks.

## CI

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs build + format-lint + tests, and
compiles the Bicep. A `what-if`/deploy stage is added in Phase 2 when the sandbox + OIDC creds
exist.

## Status

**T1 (scaffold) complete.** Next: **T2** (flesh out contracts) and **T3** (mock behaviors),
which unblock everything else. Open Decisions (stack confirmed: .NET + Bicep) remain in
[`CLAUDE.md`](CLAUDE.md) §"Open decisions" — surface, don't guess.
