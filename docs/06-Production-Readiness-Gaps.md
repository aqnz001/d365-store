# 06 — Production-Readiness Gaps

> Honest answer to "did we build sign-up/sign-in/reset/profile/invoices/billing/payment-methods/SSO,
> and what else is missing for production?" — from a 7-domain audit of the actual code.
> **Short version: no — most of those aren't built.** The storefront today is a clean
> browse → cart → checkout → pay → order-list **happy path** (4 SPA pages: Catalog, Cart, Checkout,
> Account). It is not yet a production B2B commerce portal. The audit found **79 gaps — 12 go-live
> blockers, 34 high, 23 medium, 10 low.**

## Two things to understand first

1. **The auth *flows* are correctly delegated to Microsoft Entra External ID** (hosted sign-in /
   sign-up / password-reset / MFA pages — you don't build those). What's missing is the **SPA's
   auth UX**: no Sign-in/Sign-out control, no login/logout endpoints triggering the Entra
   challenge/`SignOutAsync`, no 401/session-expiry handling, no route guards. Today the SPA only
   exercises the **dev `X-Dev-Customer` header** — flip `Auth:Mode=Entra` and a real user has no way
   to sign in from the UI. **This is the #1 blocker cluster.**

2. **Several capabilities are built in the backend but never surfaced in the SPA** — so they're
   "connect the wire," not "build from scratch":
   - **Live order status + multi-shipment tracking**: `OrderStatusFunction`, `OrderStatusView`,
     `AccountService.GetOrderStatusAsync`, BFF `GET /api/account/orders/{ref}/status` — all built;
     the Account order list still shows a hardcoded **"Queued"** badge and never calls it.
   - **Product detail**: BFF `GET /catalog/{sku}` exists; there is **no product detail page**.
   - **Durable order history**: `DistributedOrderHistoryStore` exists (Redis) but verify it's the
     active store in prod config, else history resets on restart.

---

## A. Identity & auth UX  — _BLOCKER cluster_
| Capability | Status | Sev | Effort |
|---|---|---|---|
| Sign-in control + login challenge endpoint (`/api/auth/login` → Challenge) | missing | **blocker** | M |
| Sign-out control + `signout-oidc` (clear cookie + Entra end-session) | missing | **blocker** | M |
| Authenticated-state detection + 401 / session-expiry → redirect to login | missing | **blocker** | M |
| Route guards on Account/Cart/Checkout | missing | high | M |
| Token/session silent refresh before expiry | missing | high | M |
| Surface Entra sign-up / forgot-password policies (links) | partial | high | M |
| "Signed in as" real identity (name/email, not an account code) | partial | medium | S |
| **Disable/guard the dev header-auth bypass in prod** (a typo'd `Auth:Mode` auths every request as an arbitrary customer) | partial | high | S |
| Sign-up / reset / MFA / Microsoft-SSO **pages** | delegated (Entra) | low | — |

## B. Account, profile & billing  — _mostly MISSING_
| Capability | Status | Sev | Effort |
|---|---|---|---|
| Invoices (list + view + PDF) | missing | **blocker** | L |
| Billing history / statements / open-items aging | missing | **blocker** | L |
| Saved payment methods (Stripe SetupIntents / cards-on-file) | missing | medium | M |
| Shipping / billing **address book** | missing | high | M |
| **PO number** on orders | missing | high | S |
| Credit **limit + remaining** (numeric — not even in the contract today) | missing | high | M |
| Profile view (name/email/contacts); edit is delegated to Entra | missing/delegated | medium | S |
| B2B: multiple users per company · roles/permissions · approval workflows · spend limits | missing | high | XL |
| Order history (storefront mirror) | partial (verify durable in prod) | high | M |
| Built today: credit-standing pill, recent-orders list (status hardcoded "Queued") | built | — | — |

## C. Commerce flows & pages  — _thin; backend ahead of frontend_
| Capability | Status | Sev | Effort |
|---|---|---|---|
| **Order detail page** (lines, pricing, fulfilment timeline, tracking) — backend built, SPA not wired | partial | **blocker** | M |
| **Product detail page (PDP)** — BFF `/catalog/{sku}` exists, no page | missing | high | M |
| Server-side **search** (today: client-side substring over the whole catalog) | missing | high | L |
| Catalog **pagination / virtualization** (loads the entire catalog at once) | missing | high | M |
| Reorder / saved carts | missing | high | M |
| Order templates / requisition lists | missing | medium | L |
| Quotes / RFQ (deferred — DR-007) | missing | medium | L |
| Returns / RMA (only a placeholder link) | missing | medium | L |
| Collections / category landing pages | missing | low | M |
| Recently-viewed / wishlist | missing | low | S |

## D. Payments & tax
| Capability | Status | Sev | Effort |
|---|---|---|---|
| **Stripe webhooks** (async/SCA confirmation + order↔Stripe reconciliation) | missing | **blocker** | L |
| **3-D Secure / SCA** challenge in the SPA (PSD2 — today a hard failure) | missing | **blocker** | M |
| **Tax / VAT** calc + display (today line total = unit × qty, zero tax) | missing | **blocker** | L |
| **Net-terms / pay-on-account (PO)** checkout for approved-credit customers (today everyone is forced through card; the credit decision gates nothing on payment) | missing | **blocker** | L |
| **Idempotency on the charge** (retry/double-click can double-charge) | missing | high | S |
| Refunds (full / partial) | missing | high | M |
| Email receipts | missing | medium | M |
| Multi-currency (GBP hardcoded) | missing | low | M |
| Built today: server-authoritative amount; Stripe Card Element (SAQ-A) | built/partial | — | — |

## E. Frontend resilience & discoverability
| Capability | Status | Sev | Effort |
|---|---|---|---|
| React **error boundary** (any render error → blank white screen) | missing | **blocker** | S |
| **404 / not-found** + router error route (unknown URL → blank page) | missing | high | S |
| Global API error/retry/toast layer (401 shows a raw status string) | partial | high | M |
| Route code-splitting / lazy (whole app incl. Stripe SDK on first load) | missing | medium | S |
| Catalog data strategy (triple-fetched independently by 3 components) | missing | high | L |
| Favicon / web manifest / PWA / app icons | missing | medium | S |
| SEO / meta / OpenGraph; collections for crawlability | missing | low/medium | M |
| i18n / localization / RTL (English/GBP only) | missing | low | XL |
| Accessibility: skip-link on every page + a formal WCAG audit | partial | medium | M |
| Real product **imagery pipeline** (today: generated line-art) | partial | medium | M |
| Built today: good drawer focus mgmt, sr-only live regions, reduced-motion | built | — | — |

## F. Ops, security & compliance
| Capability | Status | Sev | Effort |
|---|---|---|---|
| **Structured request logging** on the BFF (it emits zero logs today) | missing | high | M |
| App Insights / OpenTelemetry actually wired into the **BFF** (no SDK pkg) | partial | high | M |
| Monitoring dashboards + alerting + SLOs (DLQ growth, payment failures, reserve shortfalls) | missing | high | L |
| **Rate limiting / throttling** on the BFF + public API | missing | high | M |
| **CSP** + full security headers on the SPA (loads Stripe.js + checkout) | partial | high | M |
| **CSRF / anti-forgery** on the cookie+OIDC BFF session (SameSite) | missing | high | M |
| Stripe webhook **signature verification** | missing | high | M |
| **Audit logging** of order + credit actions | missing | high | M |
| GDPR: data-subject access/erasure, retention policy, DPA | missing | high | L |
| **Privacy / terms / cookie-consent** pages + banner (EU lawful go-live) | missing | medium | M |
| WAF / bot protection at the edge (Front Door) | missing | medium | L |
| Backup/restore + DR runbook (RTO/RPO) | missing | high | L |
| Load / performance testing (B1 BFF, Developer APIM, Redis sizing unvalidated) | missing | medium | M |
| **Security pen-test + PCI SAQ-A attestation** | partial | high | M |
| Health/readiness probes; secret-rotation process | partial | medium | S |

## G. Notifications & order lifecycle
| Capability | Status | Sev | Effort |
|---|---|---|---|
| Transactional email — order confirmation, payment receipt, shipment/tracking, invoice, credit-hold/approval, back-in-stock | missing | high | M |
| In-app notifications + notification preferences | missing | low | M |
| Customer comms when an order is delayed / backordered / cancelled (the FinOps business-events → status pipeline is internal-only today) | missing | medium | M |

---

## Recommended sequencing (highest value first)

1. **Make auth real (B-cluster, blockers).** Login/logout endpoints + challenge, 401→login,
   route guards, guard the dev bypass. Without this no real customer can sign in. _(~M each.)_
2. **Surface what's already built (cheap, high-impact).** Wire the Account order list to the live
   **order-status/tracking** endpoint, add an **order detail page**, add a **PDP** off `/catalog/{sku}`.
   These are S–M because the backend exists.
3. **Close the payment-correctness blockers.** Stripe **webhooks** + **SCA/3DS** handling +
   **charge idempotency**; add **VAT** and the **pay-on-account (net-terms)** path so approved-credit
   B2B customers don't have to use a card; email receipts.
4. **Frontend resilience.** Error boundary + 404 + global API/401 handling (small, removes
   blank-screen failure modes); favicon/manifest; route code-splitting; catalog pagination/search.
5. **Account/billing depth.** Address book + PO number (small, high B2B value); invoices/billing
   history (likely surfaced from D365 FinOps — needs the FinOps invoice/statement entities);
   numeric credit limit/remaining (needs a contract field on the pricing/credit service).
6. **Security/compliance/ops hardening.** CSRF, rate limiting, CSP, BFF logging + App Insights,
   audit logging, privacy/terms/cookie pages, GDPR processes, DR runbook, load test, pen-test/SAQ-A.
7. **B2B governance (largest).** Multiple users per company, roles, approval workflows, spend
   limits — an XL workstream; scope vs. launch needs.

_Several "missing pages" are intentionally **owned by D365 FinOps** (the system of record) — e.g.
invoices/statements are FinOps documents the portal should *surface*, not generate. Confirm per-item
whether the portal renders FinOps data or owns it._
