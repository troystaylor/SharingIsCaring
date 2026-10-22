targetScope = 'resourceGroup'

@description('Short name used to derive resource names. Keep it stable: changing it changes the public FQDN.')
@minLength(3)
@maxLength(12)
param appName string = 'atlmcp'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Container image to run. Leave the placeholder for the first provision; deploy.ps1 swaps in your build.')
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Check each caller\'s Atlassian or Bitbucket token at the door. Turn off only for a throwaway environment: the provider still checks every token on every call, but anyone could list the tools.')
param authEnabled bool = true

@description('Your Atlassian Cloud site, for example https://acme.atlassian.net. Required when Jira or Confluence is enabled. Callers sign in to this site.')
param atlassianSiteUrl string = ''

@description('Serves the Jira endpoint at /jira/mcp.')
param enableJira bool = true

@description('Serves the Confluence endpoint at /confluence/mcp.')
param enableConfluence bool = true

@description('Serves the Bitbucket endpoint at /bitbucket/mcp.')
param enableBitbucket bool = true

@description('Optional. Limits every Bitbucket tool to this workspace slug.')
param bitbucketWorkspace string = ''

@description('Registers the Jira tools that create and change issues. Leave false for a read-only Jira endpoint.')
param enableJiraWriteTools bool = false

@description('Registers the Confluence tools that create and change pages. Leave false for a read-only Confluence endpoint.')
param enableConfluenceWriteTools bool = false

@description('Registers the Bitbucket tools that comment on, open, and approve pull requests. Leave false for a read-only Bitbucket endpoint.')
param enableBitbucketWriteTools bool = false

@description('Restrict ingress to these CIDR ranges. Empty allows all.')
param allowedIpRanges array = []

@description('Origins allowed to call the server from a browser. Empty disables CORS. Desktop clients and Copilot do not need it.')
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

// The server holds no Atlassian or Bitbucket credential: each caller signs in and sends their own token.
// So there is no secret to store, and no Key Vault.

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
            { name: 'Atlassian__SiteUrl', value: atlassianSiteUrl }
            { name: 'Jira__Enabled', value: string(enableJira) }
            { name: 'Jira__EnableWriteTools', value: string(enableJiraWriteTools) }
            { name: 'Confluence__Enabled', value: string(enableConfluence) }
            { name: 'Confluence__EnableWriteTools', value: string(enableConfluenceWriteTools) }
            { name: 'Bitbucket__Enabled', value: string(enableBitbucket) }
            { name: 'Bitbucket__EnableWriteTools', value: string(enableBitbucketWriteTools) }
            { name: 'Bitbucket__Workspace', value: bitbucketWorkspace }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
            { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
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
output jiraEndpoint string = enableJira ? 'https://${app.properties.configuration.ingress.fqdn}/jira/mcp' : ''
output confluenceEndpoint string = enableConfluence ? 'https://${app.properties.configuration.ingress.fqdn}/confluence/mcp' : ''
output bitbucketEndpoint string = enableBitbucket ? 'https://${app.properties.configuration.ingress.fqdn}/bitbucket/mcp' : ''
output atlassianAuthorizationUrl string = (enableJira || enableConfluence) ? 'https://${app.properties.configuration.ingress.fqdn}/atlassian/oauth/authorize' : ''
output containerRegistryName string = registry.name
output containerRegistryEndpoint string = registry.properties.loginServer
output managedIdentityClientId string = identity.properties.clientId
