# Decision Register — Customer Parts Ordering Portal

| | |
|---|---|
| **Purpose** | Log decisions taken **on the owner's behalf** during autonomous build, so they can be reviewed and overturned asynchronously. |
| **How to use** | Skim the table. For any row you disagree with, change its **Status** to `Rejected` (or add a note) and tell me — I'll rework and cascade the change. Contracts/abstractions are centralized so most reversals are a contained edit. |
| **Owner** | _[Solution Architect]_ |
| **Status legend** | `Decided` = chosen and being built against · `Needs owner confirm` = built behind an abstraction but commercially/contractually needs sign-off · `Open` = not yet decided · `Superseded` = replaced by a later DR |

> Decisions are **provisional and reversible**. Anything marked `Needs owner confirm` is isolated behind an interface so swapping it is a one-place change.

---

## Summary table

| ID | Date | Decision | Status |
|---|---|---|---|
| DR-001 | 2026-06-26 | **Storefront = custom web app** (drop Shopify Plus). Supersedes SAD ADR-1. | Decided (owner-directed) |
| DR-002 | 2026-06-26 | Frontend = **React + TypeScript SPA (Vite)**; **ASP.NET Core BFF** (.NET 10) in front of APIM. | Decided |
| DR-003 | 2026-06-26 | Payments via a **hosted provider, PCI scope SAQ-A** (no raw card data on our servers); **Stripe** default behind an `IPaymentProvider` abstraction. | Needs owner confirm |
| DR-004 | 2026-06-26 | Customer identity = **Microsoft Entra External ID**; OIDC terminated at the BFF (confidential client, tokens server-side). | Needs owner confirm |
| DR-005 | 2026-06-26 | The custom app **owns its catalog read-store** (BYOD → storefront catalog). Phase-1 `shopify-sim` is reframed as the storefront-catalog stand-in; renamed when the real store is built. | Decided |
| DR-006 | 2026-06-26 | Hosting = **Azure Static Web Apps** (SPA) + **App Service/Container Apps** (BFF), behind APIM/Front Door. IaC added in the storefront-infra task. | Decided |
| DR-007 | 2026-06-26 | Phase-1 B2B scope = company accounts, contract-price display, credit/net-terms, min-qty/order-multiple/UoM validation. Recurring orders + quotes **deferred**. | Decided |
| DR-008 | 2026-06-26 | Storefront workstream **re-planned** into tasks S1–S7 (replacing the Shopify-extension task T8). | Decided |
| DR-009 | 2026-06-26 | Soft reservation at the checkout gate is **all-or-nothing**: if any line falls short, partial reservations are released and per-line shortfall options are returned. | Decided |

---

## Detail

### DR-001 — Storefront platform: custom web app
**Decision.** Build a custom storefront web app instead of Shopify Plus. Supersedes **SAD ADR-1** (Shopify Plus).
**Why.** Owner: Shopify Plus subscription too expensive.
**Implications.** We now build what Shopify provided: cart, checkout, payments (see DR-003), B2B primitives (DR-007), customer accounts, hosting. The **Azure integration layer is unaffected** — it was deliberately storefront-agnostic (SAD P2; the middleware OpenAPI is a plain HTTP contract any client calls), so T1–T5 carry over. Net change concentrates in the `storefront/` tier and the catalog/fulfilment **target** (DR-005).
**Alternatives considered.** Mid-tier commerce engine (non-Plus Shopify / BigCommerce / headless commercetools / Medusa.js) — keeps buying the PCI-hard checkout while swapping only the storefront tier; recommended as the fallback if the custom build's cost (esp. PCI) exceeds the Plus saving. Left **Open** as a pivot option since the middleware doesn't care which storefront calls it.

### DR-002 — Frontend + BFF stack
**Decision.** React + TypeScript SPA (Vite) talking to an ASP.NET Core **backend-for-frontend (BFF)** on .NET 10; the BFF calls the middleware via APIM using the T4 resilient clients.
**Why.** React has the richest B2B-commerce component ecosystem; a BFF keeps tokens/secrets server-side (DR-004), aggregates middleware calls, terminates auth, and avoids exposing APIM/CORS to the browser. BFF in .NET matches the chosen stack/team.
**Alternatives.** Blazor (all-.NET, weaker commerce ecosystem); Next.js SSR (heavier hosting; SEO is low-value for an auth-gated B2B catalog, so a SPA suffices).

### DR-003 — Payments & PCI
**Decision.** Use a hosted payment integration that keeps us in **PCI DSS SAQ-A** (card data entered into the provider's hosted fields/redirect; never touches our servers, Azure, or FinOps). Default **Stripe** (Payment Element / hosted Checkout), behind an `IPaymentProvider` abstraction so Adyen/Braintree/etc. swap cleanly.
**Why.** PCI is the dominant cost/risk of going custom (SAD §9/§10 previously contained it inside Shopify). SAQ-A via hosted fields minimizes scope.
**Needs owner confirm.** Provider choice + commercial terms + that SAQ-A (not SAQ-D) is acceptable. Build proceeds against the abstraction with Stripe as the reference implementation.

### DR-004 — Customer identity / auth
**Decision.** Microsoft Entra External ID for customer (B2B) identities; OIDC auth-code flow terminated at the BFF as a confidential client; tokens held server-side in the BFF session, never in the browser.
**Why.** Already an Entra/Azure shop; supports B2B/external identities and federation. Aligns with SAD §9 (Entra, least privilege).
**Needs owner confirm.** Tenant/licensing for Entra External ID vs an alternative (Auth0, etc.).

### DR-005 — Storefront catalog store
**Decision.** The custom app owns its own catalog **read-store**, fed by the existing BYOD→storefront catalog sync (T5). For Phase 1 the sync target (`mocks/shopify-sim` + `IShopifyCatalogSink`) **stands in** for that store; it is renamed to storefront-neutral names when the real catalog store/API is built (avoids churn on green T5 code now).
**Why.** Read/write separation (SAD P2) still holds — browse comes from a replica the storefront owns, never FinOps/OData.

### DR-006 — Hosting
**Decision.** SPA on Azure Static Web Apps (+ CDN); BFF on Azure App Service or Container Apps; both behind APIM/Front Door. Added to Bicep in the storefront-infra task (S7).

### DR-007 — Phase-1 B2B scope
**Decision.** Build: company/customer accounts, contract-price display (via the pricing service), credit status / net-terms gate (via the credit service), and min-qty / order-multiple / UoM cart validation (already synced as catalog attributes in T5). **Defer**: recurring/subscription orders (custom scheduler — was Shopify subscriptions) and quote-to-order. These map onto existing middleware; no new D365 contracts needed.

### DR-008 — Storefront re-plan (replaces Shopify-extension task T8)
The Shopify-specific T8 (checkout extensions) is replaced by a custom-storefront workstream. New tasks (storefront lane; integration lane T6/T7/T9/T10/T12 unchanged and storefront-agnostic):

| Task | Scope |
|---|---|
| **S1** | BFF scaffold (ASP.NET Core): APIM/middleware clients, Entra External ID auth (DR-004), session, health. |
| **S2** | Catalog & browse: storefront catalog store fed by T5 sync; SPA product list/detail showing **availability bands** (never counts). |
| **S3** | Cart: SPA cart + BFF cart state; live `/cart/validate` (min-qty/order-multiple/UoM, bands). |
| **S4** | Checkout gate: `/cart/reserve` + price/credit lock before payment; block on shortfall/credit. |
| **S5** | Payments: `IPaymentProvider` + Stripe Payment Element (SAQ-A, DR-003); on auth → `POST /order`. |
| **S6** | Account & B2B: company accounts, order history/status mirror, net-terms (DR-007). |
| **S7** | Storefront infra: SWA + App Service/Container Apps + Front Door in Bicep (DR-006). |

**Obsoleted by this plan:** `storefront/extensions/{checkout-validate,availability-display}` (Shopify checkout UI extensions) — removed/replaced when S1–S3 are built. **Golden Rule #11** (no browser storage) was a Shopify-extension constraint; for our own SPA it relaxes to "no sensitive data in browser storage; auth/session state stays in the BFF" (see CLAUDE.md).

### DR-009 — Reservation atomicity at the checkout gate
**Decision.** `/cart/reserve` reserves the whole cart **all-or-nothing** (T6). If any line cannot be fully reserved, any partial reservations already placed are released, and the response returns each line's reservation/shortfall so the storefront can offer reduce / backorder / split, then re-reserve.
**Why.** Prevents reservation leak / holding stock for a cart that cannot be committed; keeps the soft-reservation lifecycle clean (TDD §7.1). The HTTP surface returns 409 with the per-line breakdown on shortfall.
**Alternative.** Hold partial reservations and let the caller top up — rejected: risks orphaned/leaked reservations and complicates release.

---

## Earlier decisions (recorded for completeness)

| ID | Decision | Status |
|---|---|---|
| DR-000a | Stack = .NET 10 (C#) + Bicep; solution is `PartsPortal.slnx` (CPM, SDK pinned). | Decided (owner-confirmed) |
| DR-000b | Phase-1 contracts generated to C# DTOs via `tools/contract-gen`; CI drift-checked. | Decided |
| DR-000c | Reservation TTL, per-class buffers/band thresholds, SO-number ownership remain **config-driven** placeholders (SAD Open Questions). | Open |
