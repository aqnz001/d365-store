# Integration Infrastructure (Bicep)

Phase-1 infrastructure-as-code for the Customer Parts Ordering Portal integration
layer. Targets a resource group and provisions the topology described in the SAD
and TDD: API Management (sole ingress/egress — SAD §9), a Service Bus namespace
with the writeback queues/topic (TDD §3.3), a .NET-isolated Function App with a
system-assigned managed identity, Key Vault for all secrets (Golden Rule #9), and
an optional Azure Cache for Redis advisory cache (TDD §3.4).

## Layout

```
main.bicep            Resource-group-scope entry point; wires the modules below.
main.bicepparam       Non-secret defaults for a dev deployment.
modules/
  serviceBus.bicep    Namespace + orders-inbound (sessioned), orders-dlq,
                      status-outbound topic (+ default sub), reservation-release.
  apim.bicep          API Management (Developer, capacity 1) + placeholder product/API.
  functions.bicep     Storage + Consumption plan + Function App (dotnet-isolated).
  keyVault.bicep      RBAC Key Vault; optional Secrets User grant.
  redis.bicep         Azure Cache for Redis (Basic C0), deployed conditionally.
```

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
