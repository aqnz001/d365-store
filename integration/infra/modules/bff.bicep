// ---------------------------------------------------------------------------
// Storefront BFF (S7, DR-006): Linux App Service hosting the ASP.NET Core BFF.
// System-assigned identity (granted Key Vault Secrets User from main). Secret app
// settings are Key Vault references — NO literal secret values (Golden Rule #9);
// the deployer populates the named secrets in Key Vault. Calls the middleware via APIM.
// ---------------------------------------------------------------------------

@description('Short prefix applied to resource names.')
param namePrefix string

@description('Azure region.')
param location string

@description('Tags applied to resources.')
param tags object

@description('Middleware base URL (APIM gateway) the BFF calls. Must end with a trailing slash.')
param middlewareBaseUrl string

@description('Storefront catalog base URL (BYOD-synced catalog store).')
param catalogBaseUrl string

@description('Allowed SPA origin for CORS.')
param spaOrigin string

@description('Entra External ID OIDC authority for customer sign-in.')
param authEntraAuthority string

@description('Entra app registration (client) id for the BFF.')
param authEntraClientId string

@description('Key Vault name for @Microsoft.KeyVault(...) secret references.')
param keyVaultName string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Whether durable Redis-backed cart/order-history is enabled.')
param redisEnabled bool

@description('Header the BFF presents the middleware key in (APIM = Ocp-Apim-Subscription-Key).')
param middlewareApiKeyHeader string = 'Ocp-Apim-Subscription-Key'

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${namePrefix}-bff-plan'
  location: location
  tags: tags
  sku: { name: 'B1', tier: 'Basic' }
  kind: 'linux'
  properties: { reserved: true }
}

var baseSettings = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
  { name: 'KeyVault__Uri', value: 'https://${keyVaultName}${az.environment().suffixes.keyvaultDns}/' }
  // Auth — Entra External ID OIDC; the confidential-client secret is a KV reference.
  { name: 'Auth__Mode', value: 'Entra' }
  { name: 'Auth__Entra__Authority', value: authEntraAuthority }
  { name: 'Auth__Entra__ClientId', value: authEntraClientId }
  { name: 'Auth__Entra__ClientSecret', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=bff-entra-client-secret)' }
  // Payments — Stripe (SAQ-A); secret key from Key Vault.
  { name: 'Payments__Provider', value: 'Stripe' }
  { name: 'Payments__Stripe__SecretKey', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=stripe-secret-key)' }
  // Downstream — middleware via APIM (subscription key from KV) + storefront catalog.
  { name: 'Bff__MiddlewareBaseUrl', value: middlewareBaseUrl }
  { name: 'Bff__CatalogBaseUrl', value: catalogBaseUrl }
  { name: 'Bff__SpaOrigin', value: spaOrigin }
  { name: 'Bff__MiddlewareApiKeyHeader', value: middlewareApiKeyHeader }
  { name: 'Bff__MiddlewareApiKey', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=apim-subscription-key)' }
]

// Durable cart + order-history when Redis is provisioned (connection string from KV).
var redisSettings = redisEnabled ? [
  { name: 'Redis__ConnectionString', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=redis-connection-string)' }
] : []

resource site 'Microsoft.Web/sites@2024-04-01' = {
  name: '${namePrefix}-bff'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      appSettings: union(baseSettings, redisSettings)
    }
  }
}

output principalId string = site.identity.principalId
output siteName string = site.name
output defaultHostName string = site.properties.defaultHostName
