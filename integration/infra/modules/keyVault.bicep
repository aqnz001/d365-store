// ---------------------------------------------------------------------------
// Key Vault — single home for all integration secrets (Golden Rule #9).
// RBAC authorization model (no access policies). NO secret VALUES are ever
// declared in this template; secrets are written out-of-band by operators/CI.
// An optional principalId can be granted Secrets User (get/list) at deploy.
// ---------------------------------------------------------------------------

@description('Short prefix applied to the vault name.')
param namePrefix string

@description('Azure region for the vault.')
param location string

@description('Tags applied to the vault.')
param tags object

@description('Soft-delete retention window in days.')
@minValue(7)
@maxValue(90)
param softDeleteRetentionInDays int = 90

// Vault names: 3-24 chars, alphanumeric + hyphens. Keep deterministic + unique.
var keyVaultName = take('${namePrefix}-kv-${uniqueString(resourceGroup().id)}', 24)

resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenant().tenantId
    enableRbacAuthorization: true // RBAC model — no access policies
    enableSoftDelete: true
    softDeleteRetentionInDays: softDeleteRetentionInDays
    enablePurgeProtection: true
    publicNetworkAccess: 'Disabled' // ingress via private endpoint / APIM only
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
  }
}

// The Function App MI is granted Secrets User via the separate
// keyVaultRoleAssignment module (avoids a keyVault<->functions dependency cycle).

output keyVaultName string = keyVault.name
output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
