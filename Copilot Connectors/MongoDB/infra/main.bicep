@description('Base name for all resources.')
param name string = 'mongo-copilot'

@description('Location for all resources.')
param location string = resourceGroup().location

@description('Container registry hosting the two images.')
param registryName string

@description('Federated MCP server image tag.')
param federatedImage string

@description('Indexer image tag.')
param indexerImage string

@description('Entra tenant ID.')
param tenantId string

@description('Client ID of the app registration used for Entra SSO on the MCP server.')
param appClientId string

@description('MongoDB connection string. With mongoAuthMode ManagedIdentity this carries the host only and holds no password.')
@secure()
param mongoConnectionString string

@description('How the connector authenticates to MongoDB. ManagedIdentity requires Azure DocumentDB with Microsoft Entra auth enabled, or Atlas workload identity federation.')
@allowed([
  'ConnectionString'
  'ManagedIdentity'
])
param mongoAuthMode string = 'ConnectionString'

@description('Entra audience for the database token. Leave empty for Azure DocumentDB; required for Atlas (the Application ID URI backing workload identity federation).')
param mongoTokenResource string = ''

@description('Default MongoDB database for profiles that do not name one.')
param mongoDatabase string

@description('Base URL where documents are viewable, used for Copilot citations.')
param uiBaseUrl string

@description('Set false only to deliberately publish uncited items.')
param requireUrl bool = true

@description('Existing VNet subnet ID for the Container Apps environment. Required to reach a private endpoint.')
param infrastructureSubnetId string = ''

@description('Private DNS zones to link to the VNet, for example privatelink.mongocluster.cosmos.azure.com for Azure DocumentDB or the Atlas private endpoint zone.')
param privateDnsZoneIds array = []

var useVnet = !empty(infrastructureSubnetId)
var useManagedIdentity = mongoAuthMode == 'ManagedIdentity'

// environment() keeps the template valid in sovereign clouds, where the Entra
// authority host differs from the public one.
var authority = '${environment().authentication.loginEndpoint}${tenantId}/v2.0'

// Under managed identity there is no password anywhere in the deployment, so the
// connection string is passed as a plain setting rather than a Container App secret.
var mongoSecrets = useManagedIdentity ? [] : [
  { name: 'mongo-connection', value: mongoConnectionString }
]

var mongoAuthEnv = useManagedIdentity ? [
  { name: 'Mongo__ConnectionString', value: mongoConnectionString }
  { name: 'Mongo__AuthMode', value: 'ManagedIdentity' }
  { name: 'Mongo__TokenResource', value: mongoTokenResource }
] : [
  { name: 'Mongo__ConnectionString', secretRef: 'mongo-connection' }
  { name: 'Mongo__AuthMode', value: 'ConnectionString' }
]

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${name}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${name}-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${name}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: useVnet ? {
      infrastructureSubnetId: infrastructureSubnetId
      internal: false
    } : null
  }
}

// Private endpoints only work if the container can resolve the cluster hostname to its
// private IP. Both Azure DocumentDB and Atlas use mongodb+srv, which performs an SRV
// lookup, so the private DNS zone must be linked to the VNet the environment runs in.
resource dnsLinks 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = [for (zoneId, i) in privateDnsZoneIds: if (useVnet) {
  name: '${last(split(zoneId, '/'))}/${name}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      // The environment subnet is .../virtualNetworks/<vnet>/subnets/<subnet>; the VNet is
      // its parent resource.
      id: substring(infrastructureSubnetId, 0, indexOf(infrastructureSubnetId, '/subnets/'))
    }
  }
}]

// ── Federated MCP server ────────────────────────────────────────────────────
// Scales to zero when idle: each Copilot request is short-lived and stateless.
resource federated 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${name}-mcp'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      secrets: mongoSecrets
      registries: [
        { server: registry.properties.loginServer, identity: 'system' }
      ]
    }
    template: {
      containers: [
        {
          name: 'mcp'
          image: federatedImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat([
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'Auth__Authority', value: authority }
            { name: 'Auth__ValidAudiences__0', value: appClientId }
            { name: 'Auth__ValidAudiences__1', value: 'api://${appClientId}' }
            { name: 'Mongo__Database', value: mongoDatabase }
            { name: 'Ui__BaseUrl', value: uiBaseUrl }
            { name: 'Ui__RequireUrl', value: string(requireUrl) }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
          ], mongoAuthEnv)
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 5 }
    }
  }
}

// ── Indexer ─────────────────────────────────────────────────────────────────
// Always on: the change stream tail holds an open cursor rather than polling, so it
// cannot scale to zero without losing its position.
resource indexer 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${name}-indexer'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      secrets: mongoSecrets
      registries: [
        { server: registry.properties.loginServer, identity: 'system' }
      ]
    }
    template: {
      containers: [
        {
          name: 'indexer'
          image: indexerImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat([
            { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'Graph__TenantId', value: tenantId }
            { name: 'Mongo__Database', value: mongoDatabase }
            { name: 'Ui__BaseUrl', value: uiBaseUrl }
            { name: 'Ui__RequireUrl', value: string(requireUrl) }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
          ], mongoAuthEnv)
          // The indexer has no ingress, but Container Apps probes run against the container
          // port regardless. Without this a wedged change-stream tail would look healthy and
          // never be restarted.
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080 }
              initialDelaySeconds: 30
              periodSeconds: 60
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

@description('Base URL to register in the M365 admin center as the MCP server endpoint.')
output mcpBaseUrl string = 'https://${federated.properties.configuration.ingress.fqdn}'

@description('Grant this principal the Graph application permissions ExternalConnection.ReadWrite.OwnedBy, ExternalItem.ReadWrite.OwnedBy, plus User.Read.All and Group.Read.All for resolving document values to Entra object IDs.')
output indexerPrincipalId string = indexer.identity.principalId

@description('Grant this principal AcrPull on the registry.')
output federatedPrincipalId string = federated.identity.principalId


