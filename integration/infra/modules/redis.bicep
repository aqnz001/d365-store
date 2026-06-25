// ---------------------------------------------------------------------------
// Azure Cache for Redis — short-TTL advisory cache only (TDD §3.4).
// NEVER authoritative for inventory; IVS remains the sole availability and
// reservation authority (Golden Rule #2/#4). TTLs come from app configuration,
// not from this template. Access keys are NOT emitted; consumers authenticate
// via Entra ID / managed identity and read any required secret from Key Vault.
// ---------------------------------------------------------------------------

@description('Short prefix applied to the cache name.')
param namePrefix string

@description('Azure region for the cache.')
param location string

@description('Tags applied to the cache.')
param tags object

@description('Redis SKU family (C = Basic/Standard).')
@allowed([
  'C'
])
param skuFamily string = 'C'

@description('Redis SKU name.')
@allowed([
  'Basic'
  'Standard'
])
param skuName string = 'Basic'

@description('Redis capacity. 0 = C0 (smallest).')
@minValue(0)
@maxValue(6)
param skuCapacity int = 0

var redisName = '${namePrefix}-redis-${uniqueString(resourceGroup().id)}'

resource redis 'Microsoft.Cache/redis@2024-11-01' = {
  name: redisName
  location: location
  tags: tags
  properties: {
    sku: {
      name: skuName
      family: skuFamily
      capacity: skuCapacity
    }
    minimumTlsVersion: '1.2'
    enableNonSslPort: false
    redisConfiguration: {
      'aad-enabled': 'true' // Entra ID auth — avoids distributing access keys
    }
  }
}

output redisName string = redis.name
output redisId string = redis.id
output redisHostName string = redis.properties.hostName
