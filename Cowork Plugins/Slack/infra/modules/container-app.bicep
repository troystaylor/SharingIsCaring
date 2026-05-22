param location string
param resourceToken string
param tags object
param environmentId string

@secure()
param appInsightsConnectionString string

param keyVaultName string
param keyVaultId string
param deployerPrincipalId string

@description('Slack OAuth client id (public).')
param slackClientId string = ''

@description('Comma-separated Slack bot OAuth scopes.')
param slackBotScopes string = ''

@description('Comma-separated Slack user OAuth scopes (drive the xoxp-* user token).')
param slackUserScopes string = ''

@description('Client id the Cowork Plugin Vault sends to the OAuth shim.')
param coworkClientId string = 'slack-cowork-shim'

@secure()
@description('Slack OAuth client secret (used by the shim at /oauth/callback).')
param slackClientSecret string = ''

@secure()
@description('Client secret the Cowork Plugin Vault sends at /oauth/token.')
param coworkClientSecret string = ''

@description('Entra tenant ID used by the protected /mcp/a365 JWT validation.')
param entraTenantId string = ''

@description('Expected JWT audience (app ID URI) for /mcp/a365.')
param entraAudience string = ''

// Container Registry to hold the MCP image built by `azd up`.
// ACR names must be 5-50 alphanumerics; pad with a stable prefix so short
// resourceToken values (e.g. dev tokens) still meet the 5-char minimum.
var acrName = toLower('acrmcp${resourceToken}')
resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

// Initial image. azd replaces this on first deployment.
// Must listen on 8080 (matches ingress.targetPort) for first-revision health.
// mcr.microsoft.com/dotnet/samples:aspnetapp (latest = .NET 9+) listens on 8080.
var initialImage = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-${resourceToken}'
  location: location
  tags: union(tags, { 'azd-service-name': 'mcp' })
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: 'system'
        }
      ]
      secrets: [
        {
          name: 'appinsights-connectionstring'
          value: appInsightsConnectionString
        }
        {
          name: 'slack-client-secret'
          value: slackClientSecret
        }
        {
          name: 'cowork-client-secret'
          value: coworkClientSecret
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'mcp'
          image: initialImage
          resources: {
            cpu: json('0.5')
            memory: '1.0Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              secretRef: 'appinsights-connectionstring'
            }
            {
              name: 'KEY_VAULT_URI'
              value: 'https://${keyVaultName}${environment().suffixes.keyvaultDns}'
            }
            {
              name: 'OAuthShim__SlackClientId'
              value: slackClientId
            }
            {
              name: 'OAuthShim__SlackClientSecret'
              secretRef: 'slack-client-secret'
            }
            {
              name: 'OAuthShim__SlackBotScopes'
              value: slackBotScopes
            }
            {
              name: 'OAuthShim__SlackUserScopes'
              value: slackUserScopes
            }
            {
              name: 'OAuthShim__CoworkClientId'
              value: coworkClientId
            }
            {
              name: 'OAuthShim__CoworkClientSecret'
              secretRef: 'cowork-client-secret'
            }
            {
              name: 'EntraAuth__TenantId'
              value: entraTenantId
            }
            {
              name: 'EntraAuth__Audience'
              value: entraAudience
            }
          ]
          // Probes target the real MCP server's health endpoints. Removed from
          // the initial template (which used a placeholder image lacking these
          // routes); safe to enable now that the deployed image is the real app.
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              initialDelaySeconds: 5
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http-rule'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

// AcrPull on the registry for the Container App's system-assigned MI.
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, app.id, 'acrpull')
  scope: acr
  properties: {
    principalId: app.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalType: 'ServicePrincipal'
  }
}

// Key Vault Secrets User on the Key Vault for the Container App MI.
resource kv 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}
resource kvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVaultId, app.id, 'kv-secrets-user')
  scope: kv
  properties: {
    principalId: app.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalType: 'ServicePrincipal'
  }
}

// Website Contributor on the Container App for the deployer.
resource webContrib 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(app.id, deployerPrincipalId, 'web-contrib')
  scope: app
  properties: {
    principalId: deployerPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'de139f84-1756-47ae-9be6-808fbbe84772')
    principalType: 'User'
  }
}

output containerAppId string = app.id
output principalId string = app.identity.principalId
output fqdn string = app.properties.configuration.ingress.fqdn
output acrLoginServer string = acr.properties.loginServer
output acrName string = acr.name
