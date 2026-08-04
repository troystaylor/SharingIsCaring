targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the azd environment. The Container Apps environment derives the public FQDN suffix from this, so changing it changes every published URL — keep it stable once the federated connector is registered.')
param environmentName string

@minLength(1)
@description('Azure region for all resources.')
param location string

@description('Directory (tenant) ID of the Entra tenant the MCP server authenticates against.')
param entraTenantId string

@description('Application (client) ID of the server app registration that exposes this MCP server.')
param entraClientId string

@description('Additional token audiences to accept, comma-separated. Set this to the Application ID URI from the Entra SSO registration once the federated connector is registered.')
param entraExtraAudiences string = ''

@description('Whether the container app has already been deployed. Sourced from azd, which sets SERVICE_MCP_RESOURCE_EXISTS after the first successful deploy.')
param mcpExists bool = false

var tags = { 'azd-env-name': environmentName }
var resourceToken = uniqueString(subscription().id, environmentName, location)

resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module resources 'resources.bicep' = {
  name: 'resources'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
    entraTenantId: entraTenantId
    entraClientId: entraClientId
    entraExtraAudiences: entraExtraAudiences
    mcpExists: mcpExists
  }
}

// azd reads this to decide where to push the image. Without it, azd falls back to
// looking for a local Docker registry and the deploy fails confusingly.
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.containerRegistryEndpoint
output AZURE_RESOURCE_GROUP string = rg.name
output SERVICE_MCP_URI string = resources.outputs.mcpUri
output MCP_ENDPOINT string = '${resources.outputs.mcpUri}/mcp'

// Subject for the federated identity credential that lets the server authenticate without a secret.
output MCP_IDENTITY_PRINCIPAL_ID string = resources.outputs.identityPrincipalId
