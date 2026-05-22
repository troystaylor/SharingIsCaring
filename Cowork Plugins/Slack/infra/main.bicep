// Slack Cowork MCP — root Bicep
//
// Subscription-scoped: creates a resource group and orchestrates modules that
// deploy a Log Analytics workspace, Application Insights (workspace-based),
// a user-assigned-by-default Container Apps environment, the MCP Container
// App with system-assigned identity, Key Vault holding the Slack OAuth
// client secret, an APIM instance for the federated route, and an Entra
// application registration that backs federated SSO.

targetScope = 'subscription'

@minLength(1)
@maxLength(48)
@description('Short environment name used to derive resource names.')
param environmentName string

@minLength(1)
@description('Azure region for all resources.')
param location string

@description('Object id of the principal running the deployment. Granted RBAC on the RG, Container App, and Key Vault.')
param deployerPrincipalId string

@secure()
@description('Slack OAuth client secret. Stored in Key Vault; not used by the MCP at runtime, kept for completeness and future federated flows.')
param slackClientSecret string

@description('Slack OAuth client id. Public; injected as an env var on the Container App.')
param slackClientId string = ''

@description('Comma-separated Slack user OAuth scopes that produce the xoxp-* user token.')
param slackUserScopes string = ''

@description('Comma-separated Slack bot OAuth scopes. Empty means user-token-only.')
param slackBotScopes string = ''

@description('Client id the Cowork Plugin Vault sends to the OAuth shim.')
param coworkClientId string = 'slack-cowork-shim'

@secure()
@description('Client secret the Cowork Plugin Vault sends to the OAuth shim. Stored in Key Vault.')
param coworkClientSecret string

@allowed([
  'Developer'
  'Basic'
  'Standard'
  'Premium'
])
@description('APIM SKU. Use Developer for non-prod.')
param apimSku string = 'Developer'

@description('APIM publisher email (required by APIM, shown in dev portal).')
param apimPublisherEmail string

@description('APIM publisher display name.')
param apimPublisherName string

@description('Entra tenant ID that issues JWT tokens for the protected /mcp/a365 route.')
param entraTenantId string = subscription().tenantId

@description('Expected JWT audience (app ID URI) for the protected /mcp/a365 route.')
param entraAudience string = ''

var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var tags = {
  'azd-env-name': environmentName
  workload: 'slack-cowork-mcp'
}

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

// Deployer gets Contributor on the resource group so subsequent azd ups can update resources.
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

module kv 'modules/key-vault.bicep' = {
  name: 'key-vault'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    slackClientSecret: slackClientSecret
    coworkClientSecret: coworkClientSecret
    deployerPrincipalId: deployerPrincipalId
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

module mcp 'modules/container-app.bicep' = {
  name: 'mcp-container-app'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    environmentId: env.outputs.environmentId
    appInsightsConnectionString: ai.outputs.connectionString
    keyVaultName: kv.outputs.name
    keyVaultId: kv.outputs.id
    deployerPrincipalId: deployerPrincipalId
    slackClientId: slackClientId
    slackBotScopes: slackBotScopes
    slackUserScopes: slackUserScopes
    coworkClientId: coworkClientId
    slackClientSecret: slackClientSecret
    coworkClientSecret: coworkClientSecret
    entraTenantId: entraTenantId
    entraAudience: empty(entraAudience) ? entra.outputs.identifierUri : entraAudience
  }
}

module apim 'modules/apim.bicep' = {
  name: 'apim'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    sku: apimSku
    publisherEmail: apimPublisherEmail
    publisherName: apimPublisherName
    backendUrl: 'https://${mcp.outputs.fqdn}'
    expectedAudience: entra.outputs.identifierUri
    tenantId: subscription().tenantId
  }
}

module entra 'modules/entra-app.bicep' = {
  name: 'entra-app'
  scope: rg
  params: {
    environmentName: environmentName
    resourceToken: resourceToken
  }
}

output AZURE_LOCATION string = location
output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_CONTAINER_APP_FQDN string = mcp.outputs.fqdn
output AZURE_APIM_GATEWAY_URL string = apim.outputs.gatewayUrl
output AZURE_KEY_VAULT_NAME string = kv.outputs.name
output AZURE_APP_INSIGHTS_CONNECTION_STRING string = ai.outputs.connectionString
output MCP_FULL_URL string = 'https://${mcp.outputs.fqdn}/mcp/full'
output MCP_A365_URL string = 'https://${mcp.outputs.fqdn}/mcp/a365'
output MCP_FEDERATED_URL string = '${apim.outputs.gatewayUrl}/mcp/federated'
output OAUTH_AUTHORIZE_URL string = 'https://${mcp.outputs.fqdn}/oauth/authorize'
output OAUTH_TOKEN_URL string = 'https://${mcp.outputs.fqdn}/oauth/token'
output OAUTH_CALLBACK_URL string = 'https://${mcp.outputs.fqdn}/oauth/callback'
output ENTRA_APP_INSTRUCTIONS string = entra.outputs.instructions
