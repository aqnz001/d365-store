// ---------------------------------------------------------------------------
// Service Bus — queue-backed writeback topology. TDD §3.3, Golden Rule #8.
//   orders-inbound      : sessioned (header->lines controlled concurrency)
//   orders-dlq          : explicit dead-letter parking queue
//   status-outbound     : topic for status fan-out (+ default subscription)
//   reservation-release : IVS reservation release signals (Golden Rule #2)
// No secrets emitted. Connection details are read at runtime via Key Vault /
// managed identity by consumers; this module only defines topology.
// ---------------------------------------------------------------------------

@description('Short prefix applied to resource names.')
param namePrefix string

@description('Azure region for the namespace.')
param location string

@description('Tags applied to all resources in this module.')
param tags object

@description('Max delivery attempts before a message is dead-lettered.')
@minValue(1)
@maxValue(2000)
param maxDeliveryCount int = 10

@description('Default message time-to-live (ISO 8601 duration).')
param defaultMessageTimeToLive string = 'P14D'

var namespaceName = '${namePrefix}-sb-${uniqueString(resourceGroup().id)}'

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    minimumTlsVersion: '1.2'
    disableLocalAuth: true // enforce Entra ID / managed identity auth only
  }
}

// orders-inbound: sessions required for controlled per-order concurrency so
// header and lines are processed in order and never parallel-hammered.
resource ordersInbound 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: 'orders-inbound'
  properties: {
    requiresSession: true
    maxDeliveryCount: maxDeliveryCount
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: defaultMessageTimeToLive
    lockDuration: 'PT5M'
  }
}

// orders-dlq: explicit operational parking queue for poison/expired messages
// promoted out of the per-queue dead-letter sub-queue.
resource ordersDlq 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: 'orders-dlq'
  properties: {
    requiresSession: false
    maxDeliveryCount: maxDeliveryCount
    deadLetteringOnMessageExpiration: false
    defaultMessageTimeToLive: defaultMessageTimeToLive
    lockDuration: 'PT5M'
  }
}

// status-outbound: fan-out of order/fulfilment status changes.
resource statusOutbound 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: namespace
  name: 'status-outbound'
  properties: {
    defaultMessageTimeToLive: defaultMessageTimeToLive
  }
}

resource statusOutboundDefaultSub 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: statusOutbound
  name: 'default'
  properties: {
    maxDeliveryCount: maxDeliveryCount
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: defaultMessageTimeToLive
    lockDuration: 'PT1M'
  }
}

// storefront: the subscription the Sync app's StatusSync trigger consumes
// ([ServiceBusTrigger("status-outbound", "storefront", …)]). Non-session (the
// trigger doesn't enable sessions); status events per order are idempotent.
resource statusOutboundStorefrontSub 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: statusOutbound
  name: 'storefront'
  properties: {
    maxDeliveryCount: maxDeliveryCount
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: defaultMessageTimeToLive
    lockDuration: 'PT1M'
  }
}

// reservation-release: signals to release IVS soft reservations (TTL expiry,
// cancelled checkout, failed order). IVS remains the sole authority.
resource reservationRelease 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: 'reservation-release'
  properties: {
    requiresSession: false
    maxDeliveryCount: maxDeliveryCount
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: defaultMessageTimeToLive
    lockDuration: 'PT1M'
  }
}

output namespaceName string = namespace.name
output namespaceId string = namespace.id
output ordersInboundQueueName string = ordersInbound.name
output ordersDlqQueueName string = ordersDlq.name
output statusOutboundTopicName string = statusOutbound.name
output reservationReleaseQueueName string = reservationRelease.name
