// Azure AI Image Search — ACA infrastructure
// Deploys: Container App + Azure AI Search + Storage Account

targetScope = 'resourceGroup'

@description('Base name for all resources')
param baseName string = 'ai-image-search'

@description('Azure region')
param location string = resourceGroup().location

@description('Container image (ACR)')
param containerImage string

@description('API key for the backend (set as secret)')
@secure()
param apiKey string

@description('Enable lightweight mode (URLs only, no Blob image fetching)')
param lightweightMode bool = false

@description('Azure AI Search service name')
param searchServiceName string = '${baseName}-search'

@description('Azure AI Search index name')
param searchIndex string = 'images'

@description('Storage account name')
param storageAccountName string = replace('${baseName}stor', '-', '')

@description('Blob container for images')
param blobContainer string = 'images'

@description('ACR server (e.g., myregistry.azurecr.io)')
param acrServer string = ''

@description('ACR username')
param acrUsername string = ''

@description('ACR password')
@secure()
param acrPassword string = ''

// ─── Log Analytics ────────────────────────────────────────────────────────────

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${baseName}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ─── Container App Environment ────────────────────────────────────────────────

resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${baseName}-env'
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

// ─── Container App ────────────────────────────────────────────────────────────

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${baseName}-app'
  location: location
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8000
        transport: 'http'
      }
      secrets: [
        { name: 'api-key', value: apiKey }
        { name: 'search-key', value: searchService.listAdminKeys().primaryKey }
        { name: 'blob-connection', value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=core.windows.net' }
        { name: 'acr-password', value: acrPassword }
      ]
      registries: acrServer != '' ? [
        {
          server: acrServer
          username: acrUsername
          passwordSecretRef: 'acr-password'
        }
      ] : []
    }
    template: {
      containers: [
        {
          name: 'server'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'AZURE_SEARCH_ENDPOINT', value: 'https://${searchService.name}.search.windows.net' }
            { name: 'AZURE_SEARCH_INDEX', value: searchIndex }
            { name: 'AZURE_SEARCH_KEY', secretRef: 'search-key' }
            { name: 'AZURE_BLOB_CONNECTION_STRING', secretRef: 'blob-connection' }
            { name: 'AZURE_BLOB_CONTAINER', value: blobContainer }
            { name: 'LIGHTWEIGHT_MODE', value: lightweightMode ? 'true' : 'false' }
            { name: 'API_KEY', secretRef: 'api-key' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8000
              }
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8000
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
            name: 'http-scale'
            http: { metadata: { concurrentRequests: '20' } }
          }
        ]
      }
    }
  }
}

// ─── Azure AI Search ──────────────────────────────────────────────────────────

resource searchService 'Microsoft.Search/searchServices@2024-06-01-preview' = {
  name: searchServiceName
  location: location
  sku: { name: 'basic' }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    semanticSearch: 'standard'
  }
}

// ─── Storage Account ──────────────────────────────────────────────────────────

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource imagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: blobContainer
  properties: {
    publicAccess: 'None'
  }
}

// ─── Outputs ──────────────────────────────────────────────────────────────────

output appUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output searchEndpoint string = 'https://${searchService.name}.search.windows.net'
output storageAccountName string = storageAccount.name
