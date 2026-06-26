// ---------------------------------------------------------------------------
// Storefront SPA (S7, DR-006): Azure Static Web Apps hosting the React build, behind
// Front Door/APIM. Note: Static Web Apps deploy only to a subset of regions, so the
// location is a dedicated parameter (not the resource group's region).
// ---------------------------------------------------------------------------

@description('Short prefix applied to the resource name.')
param namePrefix string

@description('Static Web Apps region (must be a SWA-supported region, e.g. westeurope).')
param location string = 'westeurope'

@description('Tags applied to the resource.')
param tags object

resource spa 'Microsoft.Web/staticSites@2024-04-01' = {
  name: '${namePrefix}-spa'
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    // App content is published from CI (the React build); no repo wired here.
    allowConfigFileUpdates: true
  }
}

output name string = spa.name
output defaultHostName string = spa.properties.defaultHostname
