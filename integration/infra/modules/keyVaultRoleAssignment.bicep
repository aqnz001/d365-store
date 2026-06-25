// ---------------------------------------------------------------------------
// Grants a principal the built-in "Key Vault Secrets User" role (get/list) on an
// existing vault. Deployed AFTER both the Key Vault and Function App modules so
// the vault name and the Function App's managed-identity principalId are known —
// passing them as PARAMETERS keeps the role-assignment name/scope resolvable at
// deployment start (avoids BCP120) and breaks the keyVault<->functions cycle.
// No secret values are exposed by an RBAC assignment. Golden Rule #9.
// ---------------------------------------------------------------------------

@description('Name of the existing Key Vault to grant access on.')
param keyVaultName string

@description('Principal (Function App managed identity) granted Secrets User get/list.')
param principalId string

// Built-in role: Key Vault Secrets User.
var keyVaultSecretsUserRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '4633458b-17de-408a-b874-0445c86b69e6'
)

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
}

resource assignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, principalId, keyVaultSecretsUserRoleId)
  scope: vault
  properties: {
    principalId: principalId
    roleDefinitionId: keyVaultSecretsUserRoleId
    principalType: 'ServicePrincipal'
  }
}
