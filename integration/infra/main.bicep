// ---------------------------------------------------------------------------
// PartsPortal — Phase 1 integration topology (resource group scope).
// Golden Rule #9: NO secrets in templates. Key Vault + managed identity only.
// All app settings that reference secrets use Key Vault references, never
// literal values. Names/SKUs/region are parameterized — no hardcoded literals.
// SAD §9 (APIM as sole ingress/egress), TDD §3.3 (messaging), §3.4 (cache).
// ---------------------------------------------------------------------------
targetScope = 'resourceGroup'

@description('Short prefix applied to all resource names (lowercase, no spaces).')
@minLength(3)
@maxLength(11)
param namePrefix string = 'partsportal'

@description('Azure region for all resources. Defaults to the resource group region.')
param location string = resourceGroup().location

@description('Deployment environment discriminator (e.g. dev, test, prod).')
@allowed([
  'dev'
  'test'
  'prod'
])
param environment string = 'dev'

@description('Deploy Azure Cache for Redis (short-TTL advisory cache, never authoritative — TDD §3.4).')
param redisEnabled bool = true

@description('Publisher email surfaced by API Management. Non-secret contact value.')
param apimPublisherEmail string

@description('Publisher organisation name surfaced by API Management.')
param apimPublisherName string

@description('Region for the storefront Static Web App (must be a SWA-supported region).')
param staticWebAppLocation string = 'westeurope'

// Common tags applied to every module's resources.
var commonTags = {
  application: 'PartsPortal'
  environment: environment
  phase: 'phase-1'
  managedBy: 'bicep'
}

// ---------------------------------------------------------------------------
// Key Vault — created first so other modules can reference its name. Secret
// VALUES are populated out-of-band (CI/operators), never by this template.
// ---------------------------------------------------------------------------
module keyVault 'modules/keyVault.bicep' = {
  name: 'keyVault'
  params: {
    namePrefix: namePrefix
    location: location
    tags: commonTags
  }
}

// ---------------------------------------------------------------------------
// Service Bus — orders-inbound (sessioned, Golden Rule #8), orders-dlq,
// status-outbound topic, reservation-release queue. TDD §3.3.
// ---------------------------------------------------------------------------
module serviceBus 'modules/serviceBus.bicep' = {
  name: 'serviceBus'
  params: {
    namePrefix: namePrefix
    location: location
    tags: commonTags
  }
}

// ---------------------------------------------------------------------------
// Redis — conditional advisory cache. TDD §3.4: short-TTL, never authoritative.
// ---------------------------------------------------------------------------
module redis 'modules/redis.bicep' = if (redisEnabled) {
  name: 'redis'
  params: {
    namePrefix: namePrefix
    location: location
    tags: commonTags
  }
}

// ---------------------------------------------------------------------------
// Function App — .NET isolated worker, system-assigned identity. App settings
// reference Key Vault for any secret (no literals). TDD integration layer.
// ---------------------------------------------------------------------------
module functions 'modules/functions.bicep' = {
  name: 'functions'
  params: {
    namePrefix: namePrefix
    location: location
    tags: commonTags
    keyVaultName: keyVault.outputs.keyVaultName
  }
}

// ---------------------------------------------------------------------------
// API Management — sole ingress/egress for the integration layer. SAD §9.
// ---------------------------------------------------------------------------
module apim 'modules/apim.bicep' = {
  name: 'apim'
  params: {
    namePrefix: namePrefix
    location: location
    tags: commonTags
    publisherEmail: apimPublisherEmail
    publisherName: apimPublisherName
  }
}

// ---------------------------------------------------------------------------
// Grant the Function App's managed identity Key Vault Secrets User (get/list)
// via a dedicated module — deployed after the vault + app so their names/ids
// are resolvable. Scoped to the vault; no secret values exposed.
// ---------------------------------------------------------------------------
module functionKvAccess 'modules/keyVaultRoleAssignment.bicep' = {
  name: 'functionKvAccess'
  params: {
    keyVaultName: keyVault.outputs.keyVaultName
    principalId: functions.outputs.functionAppPrincipalId
  }
}

// ---------------------------------------------------------------------------
// Storefront (S7, DR-001/DR-006): the custom web app — React SPA on Static Web
// Apps + the ASP.NET Core BFF on App Service. The BFF calls the integration
// layer via APIM and is granted Key Vault Secrets User for its secrets.
// ---------------------------------------------------------------------------
module bff 'modules/bff.bicep' = {
  name: 'bff'
  params: {
    namePrefix: namePrefix
    location: location
    tags: commonTags
    middlewareBaseUrl: apim.outputs.gatewayUrl
    catalogBaseUrl: ''
    spaOrigin: 'https://${spa.outputs.defaultHostName}'
  }
}

module spa 'modules/staticWebApp.bicep' = {
  name: 'spa'
  params: {
    namePrefix: namePrefix
    location: staticWebAppLocation
    tags: commonTags
  }
}

module bffKvAccess 'modules/keyVaultRoleAssignment.bicep' = {
  name: 'bffKvAccess'
  params: {
    keyVaultName: keyVault.outputs.keyVaultName
    principalId: bff.outputs.principalId
  }
}

// ---------------------------------------------------------------------------
// Outputs — key resource names/ids for downstream pipeline steps.
// ---------------------------------------------------------------------------
output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri
output serviceBusNamespaceName string = serviceBus.outputs.namespaceName
output serviceBusNamespaceId string = serviceBus.outputs.namespaceId
output functionAppName string = functions.outputs.functionAppName
output functionAppPrincipalId string = functions.outputs.functionAppPrincipalId
output apimName string = apim.outputs.apimName
output apimGatewayUrl string = apim.outputs.gatewayUrl
output redisName string = redisEnabled ? redis!.outputs.redisName : ''
output bffName string = bff.outputs.siteName
output bffHostName string = bff.outputs.defaultHostName
output spaName string = spa.outputs.name
output spaHostName string = spa.outputs.defaultHostName
