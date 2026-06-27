# Integration Infrastructure (Bicep)

Infrastructure-as-code for the Customer Parts Ordering Portal. Targets a resource
group and provisions the full topology: API Management (sole ingress/egress — SAD
§9), a Service Bus namespace with the writeback queues/topic + the `storefront`
subscription (TDD §3.3), **one .NET-isolated Function app per integration project**
(Availability, PricingCredit, Writeback, Sync, ReservationRelease) on a shared
plan, the BFF App Service, the SPA Static Web App, Key Vault for all secrets
(Golden Rule #9), Azure Cache for Redis (durable stores — DR-011), and
Application Insights + Log Analytics.

**App-setting keys match the code** (`ExternalEndpoints__*`, `Ivs__*`,
`Availability__*`, `ExternalAuth__*`, `Redis__ConnectionString`, identity-based
`ServiceBusConnection__fullyQualifiedNamespace`, …). The app managed identities
get **Key Vault Secrets User** and (Writeback/Sync) **Service Bus Data Owner**.
See [docs/05-Go-Live-Configuration.md](../../docs/05-Go-Live-Configuration.md) for
the full config/secret/access checklist.

## Layout

```
main.bicep            Resource-group entry point; wires the modules + per-app settings + role assignments.
main.bicepparam       Non-secret defaults + commented deploy knobs (auth, scopes, catalog).
modules/
  serviceBus.bicep    Namespace + orders-inbound (sessioned), orders-dlq, status-outbound
                      topic (+ default & storefront subs), reservation-release.
  serviceBusRoleAssignment.bicep  Grants a principal Azure Service Bus Data Owner (identity auth).
  apim.bicep          API Management (Developer) + product/API (OpenAPI import is a deploy-time step).
  functions.bicep     Storage + Consumption plan + one Function app per project (loop), correct app settings.
  bff.bicep           BFF App Service + full settings (Entra/Stripe/middleware-key/Redis as KV refs).
  staticWebApp.bicep  SPA Static Web App.
  keyVault.bicep      RBAC Key Vault.
  keyVaultRoleAssignment.bicep   Grants a principal Key Vault Secrets User.
  redis.bicep         Azure Cache for Redis, deployed conditionally.
  observability.bicep Log Analytics workspace + Application Insights.
```

## Remaining deploy-time step

APIM still needs the integration **OpenAPI imported** as operations with the
Function App backend + subscription-key policy (best run at deploy against the
real Function hostnames). Everything else is provisioned/wired by the template.

## Golden Rule #9 — secrets

There are **no secret values anywhere in these templates**. All secrets live in
Key Vault and are populated out-of-band (operators or CI), never committed and
never passed as parameters. App settings on the Function App use Key Vault
references (`@Microsoft.KeyVault(VaultName=...;SecretName=...)`) so the runtime
resolves secrets through its managed identity. The Function App's managed
identity is granted the built-in **Key Vault Secrets User** role (get/list only).

## Validate (Phase 1 — only requirement is that Bicep compiles)

```bash
# Compile main + all referenced modules to ARM JSON (no Azure login needed).
az bicep build --file integration/infra/main.bicep

# Or with the standalone Bicep CLI:
bicep build integration/infra/main.bicep

# Validate the parameter file resolves against the template:
az bicep build-params --file integration/infra/main.bicepparam
```

T1 only requires that the Bicep **compiles**. No Azure subscription, login, or
deployment is needed at this stage.

## What-if / deploy (Phase 2)

Provisioning to Azure is a **Phase-2** activity and requires Azure credentials
supplied via **OIDC federated credentials** in CI (never long-lived secrets).
Once authenticated against a target resource group:

```bash
# Preview changes without applying them:
az deployment group what-if \
  --resource-group <rg-name> \
  --template-file integration/infra/main.bicep \
  --parameters integration/infra/main.bicepparam

# Apply:
az deployment group create \
  --resource-group <rg-name> \
  --template-file integration/infra/main.bicep \
  --parameters integration/infra/main.bicepparam
```

Before any real deployment, populate the required Key Vault secrets (e.g.
`storage-connection-string`, `servicebus-connection-string`, `ivs-base-url`,
`odata-base-url`, `pricing-credit-base-url`) and replace the `apimPublisher*`
placeholder contact values with the real owner.

## Parameters (main.bicep)

| Parameter | Default | Notes |
|---|---|---|
| `namePrefix` | `partsportal` | Lowercase prefix for all resource names. |
| `location` | `resourceGroup().location` | Region for all resources. |
| `environment` | `dev` | `dev` \| `test` \| `prod` discriminator/tag. |
| `redisEnabled` | `true` | Conditionally deploys the Redis advisory cache. |
| `apimPublisherEmail` | _(required)_ | Non-secret APIM publisher contact. |
| `apimPublisherName` | _(required)_ | APIM publisher organisation name. |
| `catalogBaseUrl` | `''` | Storefront catalog base URL for the BFF. |
| `authEntraAuthority` / `authEntraClientId` | `''` | BFF Entra External ID OIDC. |
| `odataScope` / `ivsScope` / `pricingScope` | `''` | Outbound Entra token scopes — **blank ⇒ no token ⇒ mocks**; set to enable real D365/IVS/pricing. |
| `ivs*` / `availability*` / `priceIntegrityToleranceFraction` | DR-015/16 defaults | Tunable availability/reservation config. |

For a real deployment, set these in `main.bicepparam` (commented placeholders are
provided) and populate the Key Vault secrets listed in docs/05 §F.
