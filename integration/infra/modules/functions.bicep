// ---------------------------------------------------------------------------
// Function App — .NET isolated worker (FUNCTIONS_WORKER_RUNTIME=dotnet-isolated),
// system-assigned managed identity. App settings reference Key Vault for any
// secret (Golden Rule #9) — NO literal secret values appear here. Endpoints
// and config come from settings, not hardcoded literals.
// ---------------------------------------------------------------------------

@description('Short prefix applied to resource names.')
param namePrefix string

@description('Azure region for all resources.')
param location string

@description('Tags applied to all resources in this module.')
param tags object

@description('Name of the Key Vault holding integration secrets (for KV references).')
param keyVaultName string

@description('Storage account SKU for the Functions content/runtime store.')
@allowed([
  'Standard_LRS'
  'Standard_ZRS'
  'Standard_GRS'
])
param storageSku string = 'Standard_LRS'

// Storage account names: 3-24 chars, lowercase alphanumeric only.
var storageAccountName = take(toLower('${namePrefix}st${uniqueString(resourceGroup().id)}'), 24)
var hostingPlanName = '${namePrefix}-plan-${uniqueString(resourceGroup().id)}'
var functionAppName = '${namePrefix}-func-${uniqueString(resourceGroup().id)}'

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: storageSku
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

// Consumption (Y1 / Dynamic) plan — checkout never blocks on the ERP; work is
// queue-driven and scales to zero between bursts.
resource hostingPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: hostingPlanName
  location: location
  tags: tags
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {}
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      netFrameworkVersion: 'v10.0'
      appSettings: [
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        // Runtime/content store. The storage key is resolved via a Key Vault
        // reference — the literal secret is NEVER placed in the template.
        // The actual connection-string secret is provisioned out-of-band into
        // Key Vault under the name below (Golden Rule #9).
        {
          name: 'AzureWebJobsStorage'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=storage-connection-string)'
        }
        // Service Bus connection consumed via Key Vault reference. Preferred
        // path is managed-identity (fullyQualifiedNamespace) — this reference
        // exists for the mock/Phase-1 path and carries no literal secret.
        {
          name: 'ServiceBusConnection'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=servicebus-connection-string)'
        }
        // External endpoint base URLs (IVS / OData / pricing-credit). Values
        // are non-secret config in Phase 1 and point at mocks; resolved via
        // Key Vault references so Phase-2 sandbox swap is a one-place edit and
        // any auth material stays in the vault. TODO(T2/T3): confirm names.
        {
          name: 'Ivs__BaseUrl'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=ivs-base-url)'
        }
        {
          name: 'ODataMock__BaseUrl'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=odata-base-url)'
        }
        {
          name: 'PricingCredit__BaseUrl'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=pricing-credit-base-url)'
        }
        {
          name: 'KeyVault__Name'
          value: keyVaultName
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
      ]
    }
  }
}

output functionAppName string = functionApp.name
output functionAppId string = functionApp.id
output functionAppPrincipalId string = functionApp.identity.principalId
output storageAccountName string = storage.name
