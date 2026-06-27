// ---------------------------------------------------------------------------
// Non-secret defaults for the Phase-1 deployment. NO secret values here
// (Golden Rule #9) — all secrets live in Key Vault and are populated
// out-of-band. Override per environment via additional .bicepparam files or
// pipeline parameters.
// ---------------------------------------------------------------------------
using './main.bicep'

param namePrefix = 'partsportal'
param environment = 'dev'
param redisEnabled = true

// Publisher contact surfaced by API Management — non-secret placeholders.
// Replace with real owner contact before a real deployment.
param apimPublisherEmail = 'platform-team@example.com'
param apimPublisherName = 'Parts Portal Platform Team'

// location intentionally omitted — defaults to resourceGroup().location.

// --- Set these for a real (non-mock) deployment; blanks keep mock/dev behaviour ---
// Storefront + auth:
// param catalogBaseUrl = 'https://<catalog-host>/'
// param authEntraAuthority = 'https://<tenant>.ciamlogin.com/<tenantId>/v2.0'
// param authEntraClientId = '<bff-app-registration-client-id>'
//
// Outbound Entra token scopes (blank ⇒ no token ⇒ mocks). Set to enable real D365/IVS/pricing:
// param odataScope = 'https://<env>.operations.dynamics.com/.default'
// param ivsScope = '<ivs-app-id-uri>/.default'
// param pricingScope = '<pricing-app-id-uri>/.default'
//
// Availability/IVS tuning (DR-015/DR-016 defaults already applied in main.bicep):
// param ivsEnvironmentId = 'usmf'
// param ivsDefaultLocation = '1'
//
// All SECRETS (Entra/Stripe/Redis/Service Bus/storage/base-URLs) are populated into Key Vault
// out-of-band under the names in docs/05-Go-Live-Configuration.md §F — never here (Golden Rule #9).
