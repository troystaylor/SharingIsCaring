param location string
param tags object

// uniqueString always returns 13 characters. Declaring it lets Bicep prove the
// registry name clears its 5-character minimum.
@minLength(13)
param resourceToken string

param entraTenantId string
param entraClientId string

@description('Additional token audiences to accept, comma-separated. Holds the Application ID URI issued by the Entra SSO registration used by the federated connector.')
param entraExtraAudiences string = ''

@description('Whether the container app has already been deployed. azd sets SERVICE_MCP_RESOURCE_EXISTS once a deploy succeeds; until then the app is provisioned with the placeholder image.')
param mcpExists bool = false

var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var metricsPublisherRoleId = '3913510d-42f4-4e42-8a64-420c390055eb'

var appName = 'ca-mcp-${resourceToken}'

// Built from the environment's domain rather than the app's own ingress FQDN, which would
// be a self-reference. The server advertises this as its OAuth resource identifier, so it
// has to be the exact public URL clients call.
var publicUrl = 'https://${appName}.${env.properties.defaultDomain}'

// User-assigned, not system-assigned. A system-assigned identity does not exist until
// the app is created, so it cannot be granted AcrPull beforehand — which deadlocks the
// very first provision, because the app cannot start without pull rights.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${resourceToken}'
  location: location
  tags: tags
}

resource logs 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'log-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'acr${resourceToken}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, identity.id, acrPullRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    IngestionMode: 'LogAnalytics'
    // The connection string carries an ingestion key, which would be the only secret
    // anywhere in this deployment. Disabling local auth forces telemetry through the
    // same managed identity the server already authenticates with.
    DisableLocalAuth: true
  }
}

resource metricsPublisher 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: insights
  name: guid(insights.id, identity.id, metricsPublisherRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', metricsPublisherRoleId)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${resourceToken}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// Read the running image back so that re-provisioning is non-destructive. Without this,
// any `azd provision` rewrites the live app to the placeholder below and takes it down.
module deployedImage 'fetch-container-image.bicep' = {
  name: 'fetch-mcp-image'
  params: {
    exists: mcpExists
    name: appName
  }
}

var runningImage = deployedImage.outputs.containers[?0].?image
var appImage = runningImage ?? 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

// Probes follow the image rather than the exists flag, so a deleted-and-recreated app also
// gets the right ones. The placeholder serves nothing on /health, so an HTTP probe there
// would stop the very first revision ever reaching Ready.
var appProbes = runningImage != null
  ? [
      {
        type: 'Readiness'
        httpGet: { path: '/health', port: 8080 }
        initialDelaySeconds: 15
        periodSeconds: 10
        timeoutSeconds: 10
        failureThreshold: 3
      }
      {
        // Readiness only gates startup. Without this a wedged process keeps taking traffic.
        type: 'Liveness'
        httpGet: { path: '/health', port: 8080 }
        initialDelaySeconds: 30
        periodSeconds: 30
        timeoutSeconds: 10
        failureThreshold: 3
      }
    ]
  : [
      {
        type: 'Readiness'
        tcpSocket: { port: 8080 }
        initialDelaySeconds: 5
        periodSeconds: 10
        failureThreshold: 3
      }
    ]

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  // azd matches this tag to the service name in azure.yaml.
  tags: union(tags, { 'azd-service-name': 'mcp' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
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
          image: appImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'AzureAd__Instance', value: environment().authentication.loginEndpoint }
            { name: 'AzureAd__TenantId', value: entraTenantId }
            { name: 'AzureAd__ClientId', value: entraClientId }
            { name: 'AzureAd__Audience', value: 'api://${entraClientId}' }
            { name: 'AzureAd__ExtraAudiences', value: entraExtraAudiences }
            { name: 'Mcp__PublicUrl', value: publicUrl }
            // The server proves its identity with a token from the managed identity rather
            // than a secret. This only works once a federated identity credential whose
            // subject is this identity's principal ID exists on the Entra app.
            { name: 'AzureAd__ClientCredentials__0__SourceType', value: 'SignedAssertionFromManagedIdentity' }
            { name: 'AzureAd__ClientCredentials__0__ManagedIdentityClientId', value: identity.properties.clientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
          ]
          probes: appProbes
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

output containerRegistryEndpoint string = registry.properties.loginServer
output mcpUri string = 'https://${app.properties.configuration.ingress.fqdn}'
output identityPrincipalId string = identity.properties.principalId
