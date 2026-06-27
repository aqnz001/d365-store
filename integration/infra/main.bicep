// ---------------------------------------------------------------------------
// PartsPortal — integration + storefront topology (resource group scope).
// Golden Rule #9: NO secrets in templates. Key Vault references + managed
// identity only. App-setting KEYS match what the code binds (ExternalEndpoints__*,
// Ivs__*, Availability__*, ExternalAuth__*, Redis__ConnectionString,
// ServiceBusConnection__fullyQualifiedNamespace, …). One function app per project.
// SAD §9 (APIM ingress/egress), TDD §3.3 (messaging), §3.4 (cache). DR-014..017.
// ---------------------------------------------------------------------------
targetScope = 'resourceGroup'

@description('Short prefix applied to all resource names (lowercase, no spaces).')
@minLength(3)
@maxLength(11)
param namePrefix string = 'partsportal'

@description('Azure region for all resources. Defaults to the resource group region.')
param location string = resourceGroup().location

@description('Deployment environment discriminator.')
@allowed([ 'dev', 'test', 'prod' ])
param environment string = 'dev'

@description('Deploy Azure Cache for Redis (durable cart/order/status/reservation stores — DR-011).')
param redisEnabled bool = true

@description('Publisher email surfaced by API Management.')
param apimPublisherEmail string

@description('Publisher organisation name surfaced by API Management.')
param apimPublisherName string

@description('Region for the storefront Static Web App (must be a SWA-supported region).')
param staticWebAppLocation string = 'westeurope'

// --- Storefront / BFF config (non-secret) ----------------------------------
@description('Storefront catalog base URL (BYOD-synced catalog store) for the BFF.')
param catalogBaseUrl string = ''

@description('Entra External ID OIDC authority for customer sign-in.')
param authEntraAuthority string = ''

@description('Entra app registration (client) id for the BFF.')
param authEntraClientId string = ''

// --- Availability / IVS config (DR-015/DR-016 defaults; tune per environment) ---
param ivsEnvironmentId string = 'usmf'
param ivsDefaultLocation string = '1'
param ivsReservationTtlSeconds string = '900'
param ivsReservationSweepCron string = '0 */2 * * * *'
param availabilityClassBufferA string = '8'
param availabilityClassBufferB string = '4'
param availabilityClassBufferC string = '1'
param availabilityDefaultBuffer string = '4'
param availabilityLowStockThreshold string = '10'
param priceIntegrityToleranceFraction string = '0.05'

// --- Outbound Entra token scopes (blank ⇒ no token ⇒ mocks; set for real D365/IVS) ---
@description('Token scope for D365 OData, e.g. https://<env>.operations.dynamics.com/.default')
param odataScope string = ''
@description('Token scope for IVS.')
param ivsScope string = ''
@description('Token scope for the pricing/credit service.')
param pricingScope string = ''

var commonTags = {
  application: 'PartsPortal'
  environment: environment
  managedBy: 'bicep'
}

// ---------------------------------------------------------------------------
// Key Vault (first — others reference its name), Service Bus, Redis, Observability.
// ---------------------------------------------------------------------------
module keyVault 'modules/keyVault.bicep' = {
  name: 'keyVault'
  params: { namePrefix: namePrefix, location: location, tags: commonTags }
}

module serviceBus 'modules/serviceBus.bicep' = {
  name: 'serviceBus'
  params: { namePrefix: namePrefix, location: location, tags: commonTags }
}

module redis 'modules/redis.bicep' = if (redisEnabled) {
  name: 'redis'
  params: { namePrefix: namePrefix, location: location, tags: commonTags }
}

module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: { namePrefix: namePrefix, location: location, tags: commonTags }
}

// ---------------------------------------------------------------------------
// Function apps — one per integration project, with role-specific settings.
// Redis connection (durable stores) is a KV reference when Redis is enabled.
// ---------------------------------------------------------------------------
var redisConnSetting = redisEnabled ? [
  { name: 'Redis__ConnectionString', value: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=redis-connection-string)' }
] : []

var functionApps = [
  {
    name: 'avail'
    needsServiceBus: false
    settings: union([
      { name: 'Ivs__EnvironmentId', value: ivsEnvironmentId }
      { name: 'Ivs__DefaultLocation', value: ivsDefaultLocation }
      { name: 'Availability__ClassBuffers__A', value: availabilityClassBufferA }
      { name: 'Availability__ClassBuffers__B', value: availabilityClassBufferB }
      { name: 'Availability__ClassBuffers__C', value: availabilityClassBufferC }
      { name: 'Availability__DefaultBuffer', value: availabilityDefaultBuffer }
      { name: 'Availability__LowStockThreshold', value: availabilityLowStockThreshold }
      { name: 'ExternalAuth__Scopes__ivs', value: ivsScope }
    ], redisConnSetting)
  }
  {
    name: 'pricing'
    needsServiceBus: false
    settings: [
      { name: 'ExternalAuth__Scopes__pricing-credit', value: pricingScope }
    ]
  }
  {
    name: 'writeback'
    needsServiceBus: true
    settings: union([
      { name: 'Ivs__EnvironmentId', value: ivsEnvironmentId }
      { name: 'PriceIntegrity__ToleranceFraction', value: priceIntegrityToleranceFraction }
      { name: 'OrderIntake__QueueName', value: 'orders-inbound' }
      { name: 'ExternalAuth__Scopes__odata', value: odataScope }
      { name: 'ExternalAuth__Scopes__ivs', value: ivsScope }
    ], redisConnSetting)
  }
  {
    name: 'sync'
    needsServiceBus: true
    settings: union([
      { name: 'StatusOutbound__TopicName', value: 'status-outbound' }
      { name: 'ExternalAuth__Scopes__odata', value: odataScope }
    ], redisConnSetting)
  }
  {
    name: 'resv'
    needsServiceBus: false
    settings: union([
      { name: 'Ivs__EnvironmentId', value: ivsEnvironmentId }
      { name: 'Ivs__ReservationTtlSeconds', value: ivsReservationTtlSeconds }
      { name: 'Ivs__ReservationSweepCron', value: ivsReservationSweepCron }
      { name: 'ExternalAuth__Scopes__ivs', value: ivsScope }
    ], redisConnSetting)
  }
]

// Start-time-only view of the apps (names + needsServiceBus, no runtime KV refs) so the
// role-assignment loops have a value calculable at the start of deployment. Order MUST match
// functionApps so index i aligns with functions.outputs.appPrincipals.
var functionAppNames = [
  { name: 'avail', needsServiceBus: false }
  { name: 'pricing', needsServiceBus: false }
  { name: 'writeback', needsServiceBus: true }
  { name: 'sync', needsServiceBus: true }
  { name: 'resv', needsServiceBus: false }
]

module functions 'modules/functions.bicep' = {
  name: 'functions'
  params: {
    namePrefix: namePrefix
    location: location
    tags: commonTags
    keyVaultName: keyVault.outputs.keyVaultName
    appInsightsConnectionString: observability.outputs.connectionString
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    apps: functionApps
  }
}

// ---------------------------------------------------------------------------
// API Management — sole ingress/egress for the integration layer (SAD §9).
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
// Storefront — SPA (Static Web Apps) + BFF (App Service). The BFF calls the
// middleware via APIM and gets Key Vault Secrets User for its secrets.
// ---------------------------------------------------------------------------
module spa 'modules/staticWebApp.bicep' = {
  name: 'spa'
  params: { namePrefix: namePrefix, location: staticWebAppLocation, tags: commonTags }
}

module bff 'modules/bff.bicep' = {
  name: 'bff'
  params: {
    namePrefix: namePrefix
    location: location
    tags: commonTags
    middlewareBaseUrl: apim.outputs.gatewayUrl
    catalogBaseUrl: catalogBaseUrl
    spaOrigin: 'https://${spa.outputs.defaultHostName}'
    authEntraAuthority: authEntraAuthority
    authEntraClientId: authEntraClientId
    keyVaultName: keyVault.outputs.keyVaultName
    appInsightsConnectionString: observability.outputs.connectionString
    redisEnabled: redisEnabled
  }
}

// ---------------------------------------------------------------------------
// Role assignments — managed identities, least privilege.
//   * Key Vault Secrets User: every function app, the BFF, and APIM.
//   * Service Bus Data Owner: the function apps that send/receive (Writeback, Sync).
// (Redis uses a connection-string KV reference, so no Redis data-plane role here.)
// ---------------------------------------------------------------------------
module functionKvAccess 'modules/keyVaultRoleAssignment.bicep' = [for (app, i) in functionAppNames: {
  name: 'kv-fn-${app.name}'
  params: {
    keyVaultName: keyVault.outputs.keyVaultName
    principalId: functions.outputs.appPrincipals[i].principalId
  }
}]

module bffKvAccess 'modules/keyVaultRoleAssignment.bicep' = {
  name: 'kv-bff'
  params: {
    keyVaultName: keyVault.outputs.keyVaultName
    principalId: bff.outputs.principalId
  }
}

module apimKvAccess 'modules/keyVaultRoleAssignment.bicep' = {
  name: 'kv-apim'
  params: {
    keyVaultName: keyVault.outputs.keyVaultName
    principalId: apim.outputs.apimPrincipalId
  }
}

module functionSbAccess 'modules/serviceBusRoleAssignment.bicep' = [for (app, i) in functionAppNames: if (app.needsServiceBus) {
  name: 'sb-fn-${app.name}'
  params: {
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    principalId: functions.outputs.appPrincipals[i].principalId
    roleNameSeed: app.name
  }
}]

// ---------------------------------------------------------------------------
// Outputs — for the deployment pipeline.
// ---------------------------------------------------------------------------
output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri
output serviceBusNamespaceName string = serviceBus.outputs.namespaceName
output functionApps array = functions.outputs.appPrincipals
output apimName string = apim.outputs.apimName
output apimGatewayUrl string = apim.outputs.gatewayUrl
output appInsightsConnectionString string = observability.outputs.connectionString
output redisName string = redisEnabled ? redis!.outputs.redisName : ''
output bffName string = bff.outputs.siteName
output bffHostName string = bff.outputs.defaultHostName
output spaName string = spa.outputs.name
output spaHostName string = spa.outputs.defaultHostName
