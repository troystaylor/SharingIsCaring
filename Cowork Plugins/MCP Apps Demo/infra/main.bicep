targetScope = 'subscription'

@description('Name of the resource group')
param resourceGroupName string = 'rg-mcp-apps-demo'

@description('Azure region')
param location string = 'westus2'

@description('Container App name')
param containerAppName string = 'mcp-apps-demo'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
}

module resources 'modules/resources.bicep' = {
  scope: rg
  name: 'resources'
  params: {
    location: location
    containerAppName: containerAppName
  }
}

output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.acrLoginServer
output AZURE_RESOURCE_GROUP string = rg.name
output SERVICE_SERVER_RESOURCE_EXISTS string = 'true'
output CONTAINER_APP_FQDN string = resources.outputs.containerAppFqdn
