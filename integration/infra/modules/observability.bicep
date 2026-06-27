// ---------------------------------------------------------------------------
// Observability — Log Analytics workspace + workspace-based Application Insights.
// Wired into every Function app + the BFF via APPLICATIONINSIGHTS_CONNECTION_STRING
// so the logs and the PartsPortal.* meters/metrics have a sink. No secrets emitted.
// ---------------------------------------------------------------------------

@description('Short prefix applied to resource names.')
param namePrefix string

@description('Azure region.')
param location string

@description('Tags applied to all resources in this module.')
param tags object

@description('Log Analytics retention in days.')
@minValue(30)
@maxValue(730)
param retentionInDays int = 30

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-law-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-ai-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

output workspaceId string = workspace.id
output appInsightsName string = appInsights.name
output connectionString string = appInsights.properties.ConnectionString
