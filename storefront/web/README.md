# storefront/web — Customer Parts Ordering Portal SPA

React + TypeScript (Vite) storefront (DR-001/DR-002). It talks **only to the BFF**
(`storefront/bff`) under `/api`; the BFF terminates Entra SSO (DR-004), holds the
session cookie, and calls the integration middleware. No card data or tokens live in
the browser (Golden Rule #11).

## Pages
- **Catalog** — BYOD-synced products; add to cart.
- **Cart** — server-side cart (lives in the BFF).
- **Checkout** — the checkout gate (availability → price/credit → reserve), then pay
  (Stripe Payment Element in production — SAQ-A, DR-003; a fake provider in Phase 1).
- **Account** — order history + credit / net-terms standing.

## Develop
```
npm install
npm run dev        # http://localhost:5173, proxies /api → the BFF (VITE_BFF_URL)
npm run build      # type-check + production build
```
Run the BFF (`dotnet run --project storefront/bff`) and point `VITE_BFF_URL` at it.
Hosting: Azure Static Web Apps (SPA) behind Front Door/APIM (S7, DR-006).
