// ---------------------------------------------------------------------------
// API Management — sole ingress/egress for the integration layer (SAD §9).
// Developer SKU, capacity 1 for Phase 1. Placeholder product + API to be
// fleshed out once contracts (T2) land. System-assigned identity so APIM can
// read backend credentials from Key Vault rather than holding literal secrets.
// ---------------------------------------------------------------------------

@description('Short prefix applied to the APIM instance name.')
param namePrefix string

@description('Azure region for the APIM instance.')
param location string

@description('Tags applied to all resources in this module.')
param tags object

@description('Publisher email surfaced by APIM (non-secret contact value).')
param publisherEmail string

@description('Publisher organisation name surfaced by APIM.')
param publisherName string

var apimName = '${namePrefix}-apim-${uniqueString(resourceGroup().id)}'

resource apim 'Microsoft.ApiManagement/service@2024-05-01' = {
  name: apimName
  location: location
  tags: tags
  sku: {
    name: 'Developer'
    capacity: 1
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName
  }
}

// Placeholder product grouping the portal-facing APIs. Subscription required;
// approval flow tightened alongside the real APIs in a later task.
resource portalProduct 'Microsoft.ApiManagement/service/products@2024-05-01' = {
  parent: apim
  name: 'parts-portal'
  properties: {
    displayName: 'Parts Portal Integration'
    description: 'Phase-1 placeholder product for the parts-ordering integration APIs. TODO(T2): bind real APIs + policies.'
    subscriptionRequired: true
    approvalRequired: false
    state: 'published'
  }
}

// Placeholder API. Backend/operations/policies are added once OpenAPI
// contracts exist (T2). No backend secret is configured here.
resource integrationApi 'Microsoft.ApiManagement/service/apis@2024-05-01' = {
  parent: apim
  name: 'integration-api'
  properties: {
    displayName: 'Integration API'
    description: 'Phase-1 placeholder. Operations imported from OpenAPI contracts in T2.'
    path: 'integration'
    protocols: [
      'https'
    ]
    subscriptionRequired: true
  }
}

resource productApiLink 'Microsoft.ApiManagement/service/products/apiLinks@2024-05-01' = {
  parent: portalProduct
  name: 'integration-api-link'
  properties: {
    apiId: integrationApi.id
  }
}

output apimName string = apim.name
output apimId string = apim.id
output gatewayUrl string = apim.properties.gatewayUrl
output apimPrincipalId string = apim.identity.principalId
