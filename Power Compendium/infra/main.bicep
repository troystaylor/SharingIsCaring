// ──────────────────────────────────────────────────────────────
// Power Compendium — Azure Infrastructure
// Deploys: Storage, AI Search, ACR, Container Apps, Log Analytics
// Does NOT deploy: Azure OpenAI (must exist separately)
// ──────────────────────────────────────────────────────────────

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Unique suffix for resource names (leave empty for auto-generated)')
param nameSuffix string = uniqueString(resourceGroup().id)

@description('Azure OpenAI endpoint URL (must already exist)')
param openAiEndpoint string

@description('Azure OpenAI deployment name for chat completions')
param openAiDeploymentName string = 'gpt-4o'

@description('Container image to deploy (e.g., myacr.azurecr.io/compendium-api:v1)')
param containerImage string = ''

// ── Names ──

var storageAccountName = 'stcomp${nameSuffix}'
var searchServiceName = 'srch-comp-${nameSuffix}'
var acrName = 'crcomp${nameSuffix}'
var logWorkspaceName = 'log-comp-${nameSuffix}'
var appInsightsName = 'ai-comp-${nameSuffix}'
var acaEnvName = 'compendium-env'
var acaAppName = 'compendium-api'

// ── Storage Account ──

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'   // Required for ACA access without private endpoints
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false      // Managed identity only — no shared keys
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource wikiPagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'wiki-pages'
  properties: {
    publicAccess: 'None'
  }
}

// ── AI Search ──

resource search 'Microsoft.Search/searchServices@2024-06-01-preview' = {
  name: searchServiceName
  location: location
  sku: { name: 'basic' }
  properties: {
    hostingMode: 'default'
    replicaCount: 1
    partitionCount: 1
    authOptions: {
      aadOrApiKey: {
        aadAuthFailureMode: 'http401WithBearerChallenge'
      }
    }
  }
}

// ── Container Registry ──

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: true
  }
}

// ── Log Analytics + App Insights ──

resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logWorkspaceName
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logWorkspace.id
  }
}

// ── Container Apps Environment ──

resource acaEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: acaEnvName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logWorkspace.properties.customerId
        sharedKey: logWorkspace.listKeys().primarySharedKey
      }
    }
  }
}

// ── Container App ──

resource acaApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: acaAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: acaEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: [
        {
          server: acr.properties.loginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: acr.listCredentials().passwords[0].value
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'compendium-api'
          image: containerImage != '' ? containerImage : '${acr.properties.loginServer}/compendium-api:latest'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'BookStorage__blobServiceUri', value: 'https://${storage.name}.blob.core.windows.net' }
            { name: 'BookSearch__endpoint', value: 'https://${search.name}.search.windows.net' }
            { name: 'BookSearch__indexName', value: 'compendium-pages' }
            { name: 'LLM__provider', value: 'azure-openai' }
            { name: 'LLM__endpoint', value: openAiEndpoint }
            { name: 'LLM__deploymentName', value: openAiDeploymentName }
            { name: 'LLM__supportsJsonMode', value: 'true' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
      }
    }
  }
}

// ── RBAC Assignments ──

// Storage Blob Data Owner — read/write/delete blobs
resource storageBlobOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, acaApp.id, 'Storage Blob Data Owner')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
    principalId: acaApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Storage Account Contributor — container management
resource storageContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, acaApp.id, 'Storage Account Contributor')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '17d1049b-9a84-46fb-8f53-869881c3d3ab')
    principalId: acaApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Search Index Data Contributor — read/write search index data
resource searchIndexContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(search.id, acaApp.id, 'Search Index Data Contributor')
  scope: search
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8ebe5a00-799e-43f5-93ac-243d3dce84a7')
    principalId: acaApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Search Service Contributor — index schema management
resource searchServiceContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(search.id, acaApp.id, 'Search Service Contributor')
  scope: search
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7ca78c08-252a-4471-8644-bb5ff32d4ba0')
    principalId: acaApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Outputs ──

output storageAccountName string = storage.name
output searchServiceName string = search.name
output acrLoginServer string = acr.properties.loginServer
output acrName string = acr.name
output acaFqdn string = acaApp.properties.configuration.ingress.fqdn
output acaAppName string = acaApp.name
output acaPrincipalId string = acaApp.identity.principalId
output appInsightsConnectionString string = appInsights.properties.ConnectionString
