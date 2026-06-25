# Storefront extensions

Shopify checkout UI extension **skeletons** for the Customer Parts Ordering Portal.

> **Phase 1 status:** these are scaffolds (task **T1**) built against the **mock
> middleware** — there is **no live D365/IVS** this phase. Real wiring, tests,
> and node CI land in **T8**. Unimplemented logic is marked `TODO(T8)`.

## Extensions

| Extension | Target | Purpose |
| --- | --- | --- |
| `checkout-validate` | `purchase.checkout.delivery-address.render-before` | Blocking checkout-gate: validate availability + credit, then soft-reserve, **before payment**. |
| `availability-display` | `purchase.checkout.cart-line-item.render-after` | Shows an advisory availability **band** per line. |

## Checkout-gate flow (T8 target)

At the checkout gate, before the buyer can pay, `checkout-validate` will:

1. `POST {middlewareBaseUrl}/cart/validate` — live availability check.
   Synced/catalog stock is **advisory**; this live check is **authoritative**
   (Golden Rule #5). On a shortfall, **block** progress with a clear reason.
2. `POST {middlewareBaseUrl}/cart/reserve` — soft reservation carrying an
   **idempotency key** (Golden Rule #6). Inventory is reserved **only** through
   the IVS-backed middleware (Golden Rule #2) — never from the storefront.
3. On a **credit hold**, block progress and surface the reason.

A correlation ID is propagated cart -> reserve -> order -> fulfilment.

## Rules these skeletons follow

- **No browser storage (Golden Rule #11):** no `localStorage`, `sessionStorage`,
  `IndexedDB`, or cookies. State comes only from Shopify-supported extension
  APIs (`useApi`, `useSettings`, `useBuyerJourneyIntercept`, metafields).
- **Availability as bands (Golden Rule #4):** `availability-display` renders a
  band (In stock / Low / Backorder / Made to order) sourced from a product
  metafield — never a raw on-hand or exact count.
- **Config-driven endpoints:** the middleware base URL is read from extension
  **settings** (`shopify.extension.toml` -> `extensions.settings.fields`),
  resolved at runtime — never a hardcoded literal.
- **No secrets in the storefront:** the extension never holds credentials. The
  middleware authenticates server-side using Key Vault + managed identity.

## Local development (T8)

`npm install` and the Shopify CLI dev/CI scripts are intentionally **not** wired
up yet (no lockfile committed). They land in **T8** alongside the real
middleware calls and extension tests.
