// ---------------------------------------------------------------------------
// Grants a principal the built-in "Azure Service Bus Data Owner" role on the
// namespace (send + receive). Required because the namespace sets
// disableLocalAuth=true — SAS/connection-string auth is off, identity only.
// ---------------------------------------------------------------------------

@description('Name of the existing Service Bus namespace.')
param serviceBusNamespaceName string

@description('Principal (Function App managed identity) to grant send/receive.')
param principalId string

@description('Suffix to keep the role-assignment name unique per principal/role.')
param roleNameSeed string

// Built-in role: Azure Service Bus Data Owner.
var dataOwnerRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '090c5cfd-751d-490a-894a-3ce6f1109419'
)

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: serviceBusNamespaceName
}

resource assignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namespace.id, principalId, dataOwnerRoleId, roleNameSeed)
  scope: namespace
  properties: {
    principalId: principalId
    roleDefinitionId: dataOwnerRoleId
    principalType: 'ServicePrincipal'
  }
}
