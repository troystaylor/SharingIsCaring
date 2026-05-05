// ── Parameters ──────────────────────────────────────────────────────────────

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Unique name for this connector (used in resource names)')
param connectorName string

@description('Container image to deploy (e.g., myregistry.azurecr.io/my-mcp:latest)')
param containerImage string

@description('Azure Container Registry name (without .azurecr.io)')
param registryName string

@description('Entra ID tenant ID for authentication')
param tenantId string

@description('App registration client ID (audience for JWT validation)')
param appClientId string

@description('Upstream API base URL')
param upstreamBaseUrl string = 'https://api.example.com'

// ── Variables ───────────────────────────────────────────────────────────────

var envName = '${connectorName}-env'
var appName = '${connectorName}-mcp'
var logAnalyticsName = '${connectorName}-logs'

// ── Log Analytics ───────────────────────────────────────────────────────────

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ── Container App Environment ───────────────────────────────────────────────

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: envName
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

// ── Container App ───────────────────────────────────────────────────────────

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: '${registryName}.azurecr.io'
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: appName
          image: containerImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'McpServer__Name'
              value: connectorName
            }
            {
              name: 'Auth__Authority'
              value: 'https://login.microsoftonline.com/${tenantId}/v2.0'
            }
            {
              name: 'Auth__ValidAudiences__0'
              value: appClientId
            }
            {
              name: 'Auth__ValidAudiences__1'
              value: 'api://${appClientId}'
            }
            {
              name: 'Upstream__BaseUrl'
              value: upstreamBaseUrl
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

// ── Outputs ─────────────────────────────────────────────────────────────────

@description('The FQDN of the container app — use this as the Base URL in M365 admin center')
output mcpBaseUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'

@description('Managed identity principal ID (grant ACR pull access)')
output identityPrincipalId string = containerApp.identity.principalId
