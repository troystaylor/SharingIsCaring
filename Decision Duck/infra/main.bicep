@description('Location for all resources')
param location string = resourceGroup().location

@description('Short workload name used in resource naming')
param workloadName string = 'decisionduck'

@description('Container image for MCP server')
param containerImage string

@description('Optional ACR login server, such as myregistry.azurecr.io')
param acrServer string = ''

@description('Optional ACR username when using private registry auth')
param acrUsername string = ''

@description('Optional ACR password when using private registry auth')
@secure()
param acrPassword string = ''

@description('Container app target port')
param targetPort int = 8080

@description('Foundry/OpenAI endpoint')
param foundryEndpoint string

@description('Foundry/OpenAI model deployment name')
param foundryModel string = 'phi-4'

@description('Optional Foundry API key secret value. Leave empty when using managed auth downstream.')
@secure()
param foundryApiKey string = ''

var logAnalyticsName = '${workloadName}-law'
var appInsightsName = '${workloadName}-ai'
var containerAppsEnvName = '${workloadName}-cae'
var managedIdentityName = '${workloadName}-mi'
var containerAppName = '${workloadName}-mcp'
var keyVaultName = take(replace('${workloadName}-kv', '-', ''), 24)

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: managedIdentityName
  location: location
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    publicNetworkAccess: 'Enabled'
    enabledForTemplateDeployment: false
    softDeleteRetentionInDays: 90
  }
}

resource foundryApiKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(foundryApiKey)) {
  parent: keyVault
  name: 'foundry-api-key'
  properties: {
    value: foundryApiKey
  }
}

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppsEnvName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: targetPort
        transport: 'http'
        allowInsecure: false
      }
      registries: empty(acrServer) ? [] : [
        {
          server: acrServer
          username: acrUsername
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: concat(empty(foundryApiKey) ? [] : [
        {
          name: 'foundry-api-key'
          keyVaultUrl: foundryApiKeySecret!.properties.secretUriWithVersion
          identity: managedIdentity.id
        }
      ], empty(acrPassword) ? [] : [
        {
          name: 'acr-password'
          value: acrPassword
        }
      ])
    }
    template: {
      scale: {
        minReplicas: 1
        maxReplicas: 5
      }
      containers: [
        {
          name: 'decision-duck-mcp'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: concat([
            {
              name: 'FOUNDRY_ENDPOINT'
              value: foundryEndpoint
            }
            {
              name: 'FOUNDRY_MODEL'
              value: foundryModel
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsights.properties.ConnectionString
            }
          ], empty(foundryApiKey) ? [] : [
            {
              name: 'FOUNDRY_API_KEY'
              secretRef: 'foundry-api-key'
            }
          ])
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: targetPort
              }
              initialDelaySeconds: 15
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: targetPort
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
    }
  }
}

output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output mcpEndpoint string = 'https://${containerApp.properties.configuration.ingress.fqdn}/mcp'
output healthEndpoint string = 'https://${containerApp.properties.configuration.ingress.fqdn}/health'
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output keyVaultName string = keyVault.name
output managedIdentityResourceId string = managedIdentity.id
