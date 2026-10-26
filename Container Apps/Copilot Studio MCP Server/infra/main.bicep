targetScope = 'resourceGroup'

@description('Short name used to derive resource names. Keep it stable: changing it changes the public FQDN.')
@minLength(3)
@maxLength(12)
param appName string = 'cstudiomcp'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Container image to run. Leave the placeholder for the first provision; deploy.ps1 swaps in your build.')
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Turn off only for a throwaway environment. The server refuses to start with auth on and no tenant or client ID.')
param authEnabled bool = true

@description('Entra tenant whose access tokens this server accepts. Callers from other tenants are refused.')
param authTenantId string = ''

@description('Application (client) ID of the Entra app that is both the sign-in client for the plugin and the API for this server.')
param authClientId string = ''

@description('Restrict ingress to these CIDR ranges. Empty allows all.')
param allowedIpRanges array = []

@description('Origins allowed to call the server from a browser. Empty disables CORS. Desktop clients such as VS Code do not need it.')
param corsAllowedOrigins array = []

@description('Keep at 1 or more for interactive MCP use. Scale-to-zero adds a cold start to the first tool call.')
@minValue(0)
param minReplicas int = 1

@minValue(1)
param maxReplicas int = 5

var uniqueSuffix = uniqueString(resourceGroup().id)
var acrName = toLower('${appName}acr${uniqueSuffix}')

// The quickstart placeholder listens on 80 and has no /healthz route, so it needs different ingress and
// probes from a real build. Both flip automatically once a real image is deployed.
var isPlaceholder = startsWith(containerImage, 'mcr.microsoft.com/k8se/quickstart')
var targetPort = isPlaceholder ? 80 : 8080

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${appName}-identity'
  location: location
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

// A system-assigned identity cannot be granted AcrPull before the app exists, which deadlocks the first
// provision. A user-assigned identity can.
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, 'AcrPull')
  scope: registry
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '7f951dda-4ed3-4680-a7ca-43fe172d538d' // AcrPull
    )
  }
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${appName}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${appName}-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${appName}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: targetPort
        transport: 'auto'
        allowInsecure: false
        ipSecurityRestrictions: [
          for (range, i) in allowedIpRanges: {
            name: 'allow-${i}'
            action: 'Allow'
            ipAddressRange: range
          }
        ]
        corsPolicy: empty(corsAllowedOrigins) ? null : {
          allowedOrigins: corsAllowedOrigins
          allowedMethods: [ 'GET', 'POST', 'DELETE', 'OPTIONS' ]
          allowedHeaders: [ 'Content-Type', 'Authorization', 'Mcp-Session-Id', 'Mcp-Protocol-Version' ]
          exposeHeaders: [ 'WWW-Authenticate', 'Mcp-Session-Id' ]
          maxAge: 3600
        }
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: identity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'mcp'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'Auth__Enabled', value: string(authEnabled) }
            { name: 'Auth__TenantId', value: authTenantId }
            { name: 'Auth__ClientId', value: authClientId }
            // The identity's token is the server's proof that it is the Entra app (see the federated credential in deploy.ps1).
            { name: 'Exchange__ManagedIdentityClientId', value: identity.properties.clientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
          ]
          probes: isPlaceholder ? [
            { type: 'Liveness', tcpSocket: { port: targetPort }, periodSeconds: 30 }
            { type: 'Readiness', tcpSocket: { port: targetPort }, periodSeconds: 10 }
          ] : [
            { type: 'Liveness', httpGet: { path: '/healthz', port: targetPort }, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/ready', port: targetPort }, periodSeconds: 10 }
          ]
        }
      ]
      scale: {
        // The server is stateless, so replicas are interchangeable.
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
  dependsOn: [ acrPull ]
}

output containerAppName string = app.name
output containerAppFqdn string = app.properties.configuration.ingress.fqdn
output mcpEndpoint string = 'https://${app.properties.configuration.ingress.fqdn}/mcp'
output containerRegistryName string = registry.name
output containerRegistryEndpoint string = registry.properties.loginServer
output managedIdentityClientId string = identity.properties.clientId
output managedIdentityPrincipalId string = identity.properties.principalId
