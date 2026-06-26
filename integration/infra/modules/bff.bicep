// ---------------------------------------------------------------------------
// Storefront BFF (S7, DR-006): Linux App Service hosting the ASP.NET Core BFF.
// System-assigned identity (granted Key Vault Secrets User from main for the Entra
// client secret + Stripe keys). NO secret values here — secret app settings use Key
// Vault references, set post-deploy (Golden Rule #9). Calls the middleware via APIM.
// ---------------------------------------------------------------------------

@description('Short prefix applied to resource names.')
param namePrefix string

@description('Azure region.')
param location string

@description('Tags applied to resources.')
param tags object

@description('Middleware base URL (APIM gateway) the BFF calls.')
param middlewareBaseUrl string

@description('Storefront catalog base URL (BYOD-synced catalog store).')
param catalogBaseUrl string

@description('Allowed SPA origin for CORS.')
param spaOrigin string

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${namePrefix}-bff-plan'
  location: location
  tags: tags
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource site 'Microsoft.Web/sites@2024-04-01' = {
  name: '${namePrefix}-bff'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'Auth__Mode', value: 'Entra' }
        { name: 'Payments__Provider', value: 'Stripe' }
        { name: 'Bff__MiddlewareBaseUrl', value: middlewareBaseUrl }
        { name: 'Bff__CatalogBaseUrl', value: catalogBaseUrl }
        { name: 'Bff__SpaOrigin', value: spaOrigin }
        // Secrets (Auth__Entra__ClientSecret, Stripe keys) are added post-deploy as Key Vault
        // references (@Microsoft.KeyVault(...)) — never as literal values.
      ]
    }
  }
}

output principalId string = site.identity.principalId
output siteName string = site.name
output defaultHostName string = site.properties.defaultHostName
