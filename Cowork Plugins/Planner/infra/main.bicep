targetScope = 'subscription'

@minLength(1)
@maxLength(48)
@description('Short environment name used to derive resource names.')
param environmentName string

@minLength(1)
@description('Azure region for all resources.')
param location string

@description('Object id of the principal running the deployment. Granted RBAC on the RG and Container App.')
param deployerPrincipalId string

var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var tags = {
  'azd-env-name': environmentName
  workload: 'planner-cowork-mcp'
}

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module rgRbac 'modules/rg-rbac.bicep' = {
  name: 'rg-rbac'
  scope: rg
  params: {
    deployerPrincipalId: deployerPrincipalId
  }
}

module ai 'modules/app-insights.bicep' = {
  name: 'app-insights'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
  }
}

module env 'modules/container-apps-env.bicep' = {
  name: 'aca-env'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    logAnalyticsWorkspaceId: ai.outputs.logAnalyticsWorkspaceId
  }
}

module mcp 'modules/ca-app.bicep' = {
  name: 'mcp-container-app'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    environmentId: env.outputs.environmentId
    appInsightsConnectionString: ai.outputs.connectionString
    deployerPrincipalId: deployerPrincipalId
  }
}

output AZURE_LOCATION string = location
output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_CONTAINER_APP_FQDN string = mcp.outputs.fqdn
output AZURE_APP_INSIGHTS_CONNECTION_STRING string = ai.outputs.connectionString
output MCP_FULL_URL string = 'https://${mcp.outputs.fqdn}/mcp/full'
output MCP_FEDERATED_URL string = 'https://${mcp.outputs.fqdn}/mcp/federated'
