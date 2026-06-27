# 05 — Go-Live Configuration & Access Checklist (D365 F&O)

> **Purpose.** Everything the business must provide — access, credentials, Azure resources, and
> environment variables — to take this portal live against **real D365 Finance & Operations**.
> The application is built so that connecting to live D365/IVS/pricing is **configuration only**
> (Golden Rule #1: all endpoints/secrets are config-driven; mocks ↔ sandbox ↔ prod is a settings
> swap, no code change). The few remaining engineering tasks are listed in **§G** so there are no
> surprises.

> **Config-key convention.** Keys are shown in .NET form `Section:Key`. In Azure App Service /
> Function App **Application settings** use a double underscore: `Section__Key` (and nested
> dictionaries like `Availability:ClassBuffers:A` → `Availability__ClassBuffers__A`). Anything
> marked 🔒 is a **secret** → store in **Key Vault** and reference it; never put it in plain config.

---

## What's ready vs. what remains

| Area | State |
|---|---|
| Integration middleware (.NET Functions) + BFF | **Config-complete.** Endpoints, outbound Entra auth, APIM/function keys, Service Bus, Redis, Key Vault, idempotency, durable stores, order intake/status, events emitter — all config-driven. |
| Availability bands, reservation TTL, buffers, multi-warehouse | **Decided + config-driven** (DR-014…DR-017). |
| Payments | Stripe wired **end to end**: BFF confirms a PaymentIntent server-side, and the SPA collects the card via Stripe's hosted **Card Element** (PCI SAQ-A) when `VITE_STRIPE_PUBLISHABLE_KEY` is set (dev keeps a fake-token path). **Needs Stripe test-key verification** before go-live — see §G1. |
| Infrastructure (Bicep) | **Reconciled + compiles.** One function app per project with the **correct** app-setting keys (`ExternalEndpoints__*`, `Ivs__*`, `Availability__*`, `ExternalAuth__*`, `Redis__ConnectionString`, identity-based `ServiceBusConnection__fullyQualifiedNamespace`); Key Vault + Service Bus **role assignments** for the app MIs; the `storefront` subscription; App Insights; full BFF settings. Remaining: **APIM OpenAPI import** + populate the Key Vault secrets + `what-if`/deploy — see §G2. |
| Observability | App Insights + Log Analytics **provisioned and wired** into every app via `APPLICATIONINSIGHTS_CONNECTION_STRING`. |

---

## A. Access & credentials to request — the short ask

Hand this list to the relevant owners. Details/where-to-get are in §C–§F.

1. **Azure subscription** — Contributor on the target subscription/resource group, plus the ability to create **Key Vault role assignments** and **Entra app registrations** (or someone who can). A **CI deployment identity** (federated/OIDC service principal) with Contributor + Key Vault Secrets Officer on the RG.
2. **D365 Finance & Operations** — the **environment URL** (`https://<env>.operations.dynamics.com`), and an **Entra app registration / service principal registered in D365** (System administration → Setup → Microsoft Entra ID applications) with a security role granting **Sales order create** + read on the order/price/customer entities used.
3. **Inventory Visibility Service (IVS)** — the **IVS endpoint URL**, the **environment id** (e.g. `usmf`), the **site/location** dimension values, and an **Entra app** authorized as an IVS caller (admin-consented) with soft-reservation + allocation features enabled.
4. **Pricing/credit service** — the **endpoint URL** and its auth **scope** (if it's the custom FinOps `PortalPricing` OData service, the path + the app permission).
5. **Microsoft Entra External ID (CIAM) tenant** for customer sign-in — a **tenant**, a **sign-up/sign-in user flow**, and a **BFF app registration** (confidential web client) with redirect URIs + a client secret/cert.
6. **Stripe account** — a **live account** (PCI **SAQ-A**) with the **secret key** (and **publishable key** for the SPA), the **webhook signing secret**, and a **test-mode** key for verification.
7. **Storefront catalog source** — the **BYOD** (Azure SQL export of the D365 product master) connection, and the **catalog store** endpoint the sync writes to / the SPA reads (`Bff:CatalogBaseUrl`). (Shopify was dropped for the storefront — DR-001; confirm the real catalog target.)
8. **DNS + TLS** — the production **hostname** for the storefront and the BFF, with managed certificates; the Entra redirect URIs and the BFF `SpaOrigin` must match it.

---

## B. Azure resources to provision

| Resource | Purpose | Notes |
|---|---|---|
| **Resource group** | Holds everything | one per environment (dev/test/prod) |
| **App Service plan + App Service (Linux, .NET 10)** | Hosts the **BFF** | enable **system-assigned managed identity**; prod tier P-series + slots/autoscale |
| **Static Web App (Standard)** | Hosts the **React SPA** | wire **Linked Backend → BFF** (or Front Door) so `/api/*` reaches the BFF same-origin |
| **Function App(s) (.NET 10 isolated) + plan + Storage** | Availability, PricingCredit, Writeback, Sync, ReservationRelease | system-assigned MI each; EP/Premium plan if VNet/always-on needed |
| **Azure Service Bus (Standard)** | Queue `orders-inbound` (sessions on) + `orders-dlq`; topic `status-outbound` + subscription **`storefront`** (sessions on) | prefer **identity-based** access (see §C/§G2) |
| **Azure Cache for Redis** | Durable cart, order-history, order-status, **reservation registry** (DR-011) | **one shared instance** across the Function apps + BFF; Standard tier for SLA |
| **Azure Key Vault (RBAC)** | All secrets in §F | grant **Key Vault Secrets User** to each app's MI (and the APIM MI) |
| **API Management** | Ingress the BFF calls for the middleware functions | import the integration OpenAPI, set Function App backend + key policy; Standard+ for SLA |
| **Application Insights + Log Analytics** | Telemetry (logs + the `PartsPortal.*` metrics) | **not yet wired** — §G3 |
| **(Optional) Front Door / WAF, VNet + private endpoints, custom domains** | Edge, network isolation, hostnames | Key Vault is currently public-access-disabled in IaC; provide a network path or relax — §G2 |

---

## C. Configuration reference (non-secret) per deployable

### C.1 BFF (App Service)
| Key | Example | Notes |
|---|---|---|
| `Auth:Mode` | `Entra` | **MUST be `Entra` in prod** (any other value enables the dev header auth — blocker) |
| `Auth:Entra:Authority` | `https://<tenant>.ciamlogin.com/<tenantId>/v2.0` | Entra External ID authority |
| `Auth:Entra:ClientId` | `<guid>` | BFF app registration |
| `Bff:MiddlewareBaseUrl` | `https://<apim>.azure-api.net/parts/` | **trailing slash required** |
| `Bff:CatalogBaseUrl` | `https://<catalog-host>/` | storefront catalog store; **trailing slash** |
| `Bff:SpaOrigin` | `https://portal.contoso.com` | exact SPA origin for CORS |
| `Bff:MiddlewareApiKeyHeader` | `Ocp-Apim-Subscription-Key` | APIM key header (or `x-functions-key` if direct) |
| `Bff:CatalogApiKeyHeader` | `Ocp-Apim-Subscription-Key` | only if the catalog needs a key |
| `Payments:Provider` | `Stripe` | **MUST be `Stripe` in prod** (default `Fake` fakes success — blocker) |
| `KeyVault:Uri` | `https://kv-portal-prod.vault.azure.net/` | enables Key Vault config source via MI |
| Secrets | — | `Auth:Entra:ClientSecret` 🔒, `Payments:Stripe:SecretKey` 🔒, `Bff:MiddlewareApiKey` 🔒, `Redis:ConnectionString` 🔒 — see §F |

### C.2 Cross-cutting (every Function app + the BFF where it calls D365)
| Key | Example | Notes |
|---|---|---|
| `ExternalEndpoints:IvsBaseUrl` | `https://<ivs-host>/` | real IVS |
| `ExternalEndpoints:ODataBaseUrl` | `https://<env>.operations.dynamics.com/data/` | D365 FinOps OData |
| `ExternalEndpoints:PricingCreditBaseUrl` | `https://<pricing-host>/` | pricing/credit service |
| `ExternalEndpoints:ShopifyBaseUrl` | `https://<catalog-host>/` | catalog sink (Sync app) |
| `ExternalAuth:UseManagedIdentity` | `true` | **preferred** — use the app MI for D365/IVS tokens (no secret) |
| `ExternalAuth:TenantId` / `:ClientId` | `<guid>` | only if **not** using managed identity |
| `ExternalAuth:Scopes:odata` | `https://<env>.operations.dynamics.com/.default` | D365 OData token scope |
| `ExternalAuth:Scopes:ivs` | `<ivs-app-id-uri>/.default` | IVS token scope |
| `ExternalAuth:Scopes:pricing-credit` | `<pricing-app-id-uri>/.default` | pricing token scope (if secured) |
| `Resilience:TimeoutSeconds` / `:MaxRetryAttempts` / `:BaseDelayMilliseconds` | `30` / `3` / `200` | optional; defaults apply |
| `KeyVault:Uri` | `https://kv-portal-prod.vault.azure.net/` | functions can also pull secrets from KV |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | `InstrumentationKey=…` | telemetry (§G3) |
| `AzureWebJobsStorage` 🔒 | (storage connection or identity) | Functions runtime |

> A client with **no `ExternalAuth:Scopes:<name>` entry sends no bearer token** — that's how the
> Phase-1 mocks keep working. Setting the scope turns on Entra auth for that client. No code change.

### C.3 Availability Function app
`ExternalEndpoints:IvsBaseUrl`, `ExternalAuth:Scopes:ivs`, `Ivs:EnvironmentId` (`usmf`), `Ivs:DefaultLocation` (`11`), `Availability:ClassBuffers:A|B|C` (`8|4|1`), `Availability:DefaultBuffer` (`4`), `Availability:LowStockThreshold` (`10`), `Redis:ConnectionString` 🔒 (shared instance, for the reservation registry).

### C.4 PricingCredit Function app
`ExternalEndpoints:PricingCreditBaseUrl`, `ExternalAuth:Scopes:pricing-credit`.

### C.5 Writeback Function app
`ServiceBusConnection` 🔒 **or** `ServiceBusConnection:fullyQualifiedNamespace` (identity), `ExternalEndpoints:ODataBaseUrl`, `ExternalAuth:Scopes:odata`, `Ivs:EnvironmentId`, `Redis:ConnectionString` 🔒 (idempotency), `OrderIntake:QueueName` (`orders-inbound`), `PriceIntegrity:ToleranceFraction` (e.g. `0.05`; omit to disable the locked-price check).

### C.6 Sync Function app
`ServiceBusConnection` 🔒 (status-outbound subscription `storefront`), `ExternalEndpoints:ShopifyBaseUrl` (catalog sink), `ExternalEndpoints:ODataBaseUrl` (catalog source if OData), `Redis:ConnectionString` 🔒 (shared order-status store), `StatusOutbound:TopicName` (`status-outbound`).

### C.7 ReservationRelease Function app
`Ivs:ReservationTtlSeconds` (`900`), `Ivs:ReservationSweepCron` (`0 */2 * * * *`), `ExternalEndpoints:IvsBaseUrl`, `ExternalAuth:Scopes:ivs`, `Redis:ConnectionString` 🔒 (**same** instance as Availability — the sweep must see reservations placed there).

### C.8 SPA (Static Web App)
The SPA calls the BFF via the **relative** `/api` path (same-origin via SWA Linked Backend); `VITE_BFF_URL` is **dev-proxy only**. The one build-time client setting is **`VITE_STRIPE_PUBLISHABLE_KEY`** (`pk_test_…`/`pk_live_…`, not a secret) — set it to turn on Stripe card capture; unset uses the dev fake-token path.

---

## D. Entra app registrations & role assignments

| Identity | Grants needed |
|---|---|
| **BFF app registration** (Entra External ID, confidential web client) | Redirect URI `https://portal.contoso.com/signin-oidc`, front-channel logout URI, scopes `openid`/`profile`, a **client secret or certificate** 🔒 |
| **Entra External ID user flow** | Sign-up/sign-in policy for customer accounts |
| **D365/IVS caller** (prefer the **Function App managed identities**, else one app registration) | Registered in **D365** (Entra ID applications) with a security role granting sales-order create + entity reads; authorized as an **IVS** caller (admin-consented); pricing service permission if secured |
| **App MIs → Key Vault** | `Key Vault Secrets User` on the vault (BFF, each Function app, APIM) |
| **Function MI → Service Bus** | `Azure Service Bus Data Sender` (intake on `orders-inbound`, status publisher on `status-outbound`) + `Data Receiver` (Writeback consumer, Sync consumer) |
| **App MIs → Redis** | Redis data-plane access (Entra access policy / `Redis Cache Contributor`) if using identity-based Redis |
| **CI deployment identity** | Federated (OIDC) credential + Contributor + Key Vault Secrets Officer on the RG; SWA deployment token |

---

## E. External system access (who to ask)

| System | What we need |
|---|---|
| **D365 FinOps** | Env URL; service principal registered in D365 with the right security role; confirm `SalesOrderHeadersV2` returns the sequence-assigned SO number (DR-014); enable **fulfilment business events** (pack/ship/invoice/return/cancel) onto `status-outbound`, sessioned by sales-order number |
| **IVS** | Endpoint URL; environment id; site/location dimensions; soft-reservation + allocation enabled; Entra caller consented |
| **Pricing/credit** | Endpoint URL + auth scope (the `POST .../PortalPricing/resolve` contract is in `integration/contracts`) |
| **Entra External ID** | Tenant + user flow + BFF app registration + redirect URIs + secret |
| **Stripe** | Live + test secret keys 🔒, publishable key, webhook signing secret 🔒; confirm **SAQ-A** (hosted Payment Element) |
| **Catalog / BYOD** | BYOD Azure SQL export of the product master; the catalog store endpoint for `Bff:CatalogBaseUrl` and the sync sink |

---

## F. Secrets → Key Vault (and where to get each)

| Secret (KV name suggestion) | From |
|---|---|
| `Auth:Entra:ClientSecret` (`bff-entra-client-secret`) | Entra → App registrations → BFF → Certificates & secrets |
| `Payments:Stripe:SecretKey` (`stripe-secret-key`) | Stripe → Developers → API keys → Secret key (`sk_live_…`) |
| `Payments:Stripe:WebhookSecret` (`stripe-webhook-secret`) | Stripe → Webhooks → endpoint signing secret |
| `Bff:MiddlewareApiKey` (`apim-subscription-key`) | APIM → Subscriptions (parts-portal product) **or** Function App host key after deploy |
| `Redis:ConnectionString` (`redis-connection-string`) | Azure Cache for Redis → Access keys (or use identity) |
| `ServiceBusConnection` (`servicebus-connection-string`) | Service Bus → Shared access policies (or use `…:fullyQualifiedNamespace` + MI) |
| `ExternalAuth:ClientSecret` (`external-auth-client-secret`) | The D365/IVS caller app registration secret — **only if not using managed identity** |
| `ExternalEndpoints:*BaseUrl` | Not secret, but often kept in KV: from the D365/IVS/pricing owners |
| `AzureWebJobsStorage` (`storage-connection-string`) | Functions Storage account access key (or identity) |
| `<catalog admin token>` | Catalog store admin token, if the chosen catalog target needs one |

---

## G. Remaining engineering tasks (before "pure config")

These are **code/IaC**, not config — the honest gap between today and one-command deploy:

1. **Stripe card capture in the SPA — implemented, needs verification.** The SPA now renders Stripe's hosted **Card Element** and sends a `PaymentMethod` id (`pm_…`) to the BFF (which confirms the PaymentIntent server-side), gated on `VITE_STRIPE_PUBLISHABLE_KEY`; with no key it uses the dev fake-token path. **Remaining:** verify against a **Stripe test account** (test cards incl. a 3-D-Secure card — the SPA surfaces `requires_action` as a message but doesn't yet run the SCA challenge), set the publishable key in the SPA build and the secret key in Key Vault, and add a webhook for async confirmation if desired.
2. **IaC — done except deploy-time bits** (`integration/infra/*.bicep`, compiles with `bicep build`). **Done:** one function app per project with the **code-correct** app-setting keys; Key Vault + Service Bus **role assignments** for the app MIs; identity-based Service Bus; the **`storefront`** subscription; App Insights + Log Analytics; full BFF settings (Entra/Stripe/middleware-key/Redis as KV refs); tunable params (DR-015/16 defaults). **Remaining (deploy-time):** (a) **APIM** — import the integration OpenAPI as operations with the Function App backend + subscription-key/JWT policies (an import operation best run at deploy); (b) **populate the Key Vault secrets** (§F) before first start; (c) run `az deployment group what-if` then deploy; (d) optional prod SKUs/slots/VNet/Front Door.
3. **CatalogSync source/sink switch** — currently the sample BYOD JSON → sim sink; add a config switch to the real BYOD source + catalog sink for go-live.
4. **D365 fulfilment business events → `status-outbound`** — configure FinOps to emit pack/ship/invoice/return/cancel events (sessioned by SO number); the consumer + status mirror are ready.

---

## H. Go-live sequence

1. Provision Azure (§B) per environment; create the Entra app registrations + user flow (§D).
2. Register the D365/IVS service principal; gather endpoint URLs + scopes (§E).
3. Put all secrets in Key Vault (§F); grant the app MIs `Key Vault Secrets User`.
4. Populate the Key Vault secrets (§F); finish §G2 deploy-time bits (APIM OpenAPI import); `az deployment group what-if` then deploy.
5. Set the config (§C) — point `ExternalEndpoints:*` + `ExternalAuth:Scopes:*` at the **D365 sandbox** first; set `Auth:Mode=Entra`, `Payments:Provider=Stripe` (test key).
6. Smoke-test against the sandbox: browse → cart → live availability bands → checkout gate (reserve + price + credit) → pay (Stripe test) → order writeback → status sync. Confirm the SO number format + business events.
7. Tune buffers/TTL (DR-015/DR-016) from real telemetry; finish §G1 (Stripe Element), §G3 (catalog source) and §G4.
8. Flip `ExternalEndpoints:*`/keys to **production** D365 + Stripe live; cut DNS/TLS; go live.

---
_Generated from a per-component audit of the codebase (config keys verified against source). Update this file as §G items land._
