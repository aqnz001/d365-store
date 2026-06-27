// ---------------------------------------------------------------------------
// Function apps — one .NET-isolated app per integration project (Availability,
// PricingCredit, Writeback, Sync, ReservationRelease) on a shared Consumption
// plan + storage. System-assigned identity each. App-setting KEYS match what the
// code binds (ExternalEndpoints__*, Ivs__*, Availability__*, ExternalAuth__*,
// Redis__ConnectionString, ServiceBusConnection__fullyQualifiedNamespace, etc.).
// Secrets are Key Vault references only (Golden Rule #9). Service Bus uses
// identity (namespace has disableLocalAuth=true) — roles are granted in main.
// ---------------------------------------------------------------------------

@description('Short prefix applied to resource names.')
param namePrefix string

@description('Azure region.')
param location string

@description('Tags applied to all resources in this module.')
param tags object

@description('Key Vault name for @Microsoft.KeyVault(...) references.')
param keyVaultName string

@description('Application Insights connection string for telemetry.')
param appInsightsConnectionString string

@description('Service Bus namespace name (for identity-based ServiceBusConnection__fullyQualifiedNamespace).')
param serviceBusNamespaceName string

@description('Per-app definitions: { name, needsServiceBus, settings: [ { name, value } ] }.')
param apps array

@allowed([ 'Standard_LRS', 'Standard_ZRS', 'Standard_GRS' ])
param storageSku string = 'Standard_LRS'

var storageAccountName = take(toLower('${namePrefix}st${uniqueString(resourceGroup().id)}'), 24)

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: { name: storageSku }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource hostingPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${namePrefix}-plan-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  sku: { name: 'Y1', tier: 'Dynamic' }
  properties: {}
}

// Settings shared by every function app. Secrets are KV references; base URLs are
// kept in KV too so a Phase-2 sandbox swap is a one-place edit.
var commonSettings = [
  { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
  { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
  { name: 'WEBSITE_RUN_FROM_PACKAGE', value: '1' }
  { name: 'AzureWebJobsStorage', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=storage-connection-string)' }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
  { name: 'KeyVault__Uri', value: 'https://${keyVaultName}${az.environment().suffixes.keyvaultDns}/' }
  { name: 'ExternalEndpoints__IvsBaseUrl', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=ivs-base-url)' }
  { name: 'ExternalEndpoints__ODataBaseUrl', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=odata-base-url)' }
  { name: 'ExternalEndpoints__PricingCreditBaseUrl', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=pricing-credit-base-url)' }
  { name: 'ExternalEndpoints__ShopifyBaseUrl', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=catalog-base-url)' }
  // Outbound D365/IVS/pricing auth via the app's managed identity (no client secret).
  // Per-client token scopes are set per app where that client is used (see main).
  { name: 'ExternalAuth__UseManagedIdentity', value: 'true' }
]

// Identity-based Service Bus binding (the namespace disables local/SAS auth).
var serviceBusSetting = [
  { name: 'ServiceBusConnection__fullyQualifiedNamespace', value: '${serviceBusNamespaceName}.servicebus.windows.net' }
]

resource functionApp 'Microsoft.Web/sites@2024-04-01' = [for app in apps: {
  name: '${namePrefix}-${app.name}-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  kind: 'functionapp'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      netFrameworkVersion: 'v10.0'
      appSettings: union(commonSettings, (app.needsServiceBus ? serviceBusSetting : []), app.settings)
    }
  }
}]

output storageAccountName string = storage.name
output appPrincipals array = [for (app, i) in apps: {
  role: app.name
  appName: functionApp[i].name
  principalId: functionApp[i].identity.principalId
  defaultHostName: functionApp[i].properties.defaultHostName
  needsServiceBus: app.needsServiceBus
}]
