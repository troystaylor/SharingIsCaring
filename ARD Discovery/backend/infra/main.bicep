targetScope = 'resourceGroup'

@description('Primary location for all resources')
param location string = resourceGroup().location

@description('Environment name for resource naming')
param environmentName string

@description('Backend API key for connector authentication')
@secure()
param backendApiKey string

@description('OBO Client ID for Entra token exchange')
param oboClientId string = ''

@description('OBO Client Secret')
@secure()
param oboClientSecret string = ''

@description('OBO Tenant ID')
param oboTenantId string = ''

@description('Comma-separated domains to crawl')
param crawlDomains string = ''

@description('Publisher domain for this instance')
param publisherDomain string = ''

@description('Azure AI Search endpoint (leave empty to use Table Storage index)')
param aiSearchEndpoint string = ''

@description('Azure AI Search admin key')
@secure()
param aiSearchApiKey string = ''

@description('Azure OpenAI embedding endpoint')
param embeddingEndpoint string = ''

@description('Azure OpenAI embedding key')
@secure()
param embeddingKey string = ''

var resourceToken = toLower(uniqueString(resourceGroup().id, environmentName))
var functionAppName = 'ard-discovery-${resourceToken}'
var storageName = 'arddisc${resourceToken}'
var appInsightsName = 'ard-discovery-${resourceToken}'
var planName = 'ard-discovery-plan-${resourceToken}'
var keyVaultName = 'arddisc-${resourceToken}'

// ====================================================================
// Storage Account
// ====================================================================
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

// ====================================================================
// Application Insights
// ====================================================================
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    RetentionInDays: 90
  }
}

// ====================================================================
// Key Vault
// ====================================================================
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

resource secretBackendApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'BackendApiKey'
  properties: { value: backendApiKey }
}

resource secretOboClientSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(oboClientSecret)) {
  parent: keyVault
  name: 'OboClientSecret'
  properties: { value: oboClientSecret }
}

resource secretAiSearchApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(aiSearchApiKey)) {
  parent: keyVault
  name: 'AiSearchApiKey'
  properties: { value: aiSearchApiKey }
}

resource secretEmbeddingKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(embeddingKey)) {
  parent: keyVault
  name: 'EmbeddingKey'
  properties: { value: embeddingKey }
}

// ====================================================================
// App Service Plan (Consumption)
// ====================================================================
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: { name: 'Y1', tier: 'Dynamic' }
  properties: { reserved: false }
}

// ====================================================================
// Function App
// ====================================================================
resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  tags: { 'azd-service-name': 'api' }
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v8.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'APPINSIGHTS_INSTRUMENTATIONKEY', value: appInsights.properties.InstrumentationKey }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'TokenStoreTableUri', value: 'https://${storage.name}.table.core.windows.net' }
        { name: 'BackendApiKey', value: '@Microsoft.KeyVault(SecretUri=${secretBackendApiKey.properties.secretUri})' }
        { name: 'BackendBaseUrl', value: 'https://${functionAppName}.azurewebsites.net' }
        { name: 'EnableElicitation', value: 'false' }
        { name: 'OboClientId', value: oboClientId }
        { name: 'OboClientSecret', value: !empty(oboClientSecret) ? '@Microsoft.KeyVault(SecretUri=${secretOboClientSecret.properties.secretUri})' : '' }
        { name: 'OboTenantId', value: oboTenantId }
        { name: 'CrawlDomains', value: crawlDomains }
        { name: 'PublisherDomain', value: !empty(publisherDomain) ? publisherDomain : '${functionAppName}.azurewebsites.net' }
        { name: 'CatalogEntries', value: '' }
        { name: 'AiSearchEndpoint', value: aiSearchEndpoint }
        { name: 'AiSearchApiKey', value: !empty(aiSearchApiKey) ? '@Microsoft.KeyVault(SecretUri=${secretAiSearchApiKey.properties.secretUri})' : '' }
        { name: 'AiSearchIndexName', value: 'ard-catalog-entries' }
        { name: 'AiSearchEmbeddingEndpoint', value: embeddingEndpoint }
        { name: 'AiSearchEmbeddingKey', value: !empty(embeddingKey) ? '@Microsoft.KeyVault(SecretUri=${secretEmbeddingKey.properties.secretUri})' : '' }
        { name: 'AiSearchEmbeddingModel', value: 'text-embedding-3-small' }
      ]
    }
  }
}

// Key Vault access for Function App managed identity
resource kvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionApp.id, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Storage Blob Data Owner — required for AzureWebJobsStorage with managed identity
resource storageBlobRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b') // Storage Blob Data Owner
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Storage Queue Data Contributor — required for Functions trigger infrastructure
resource storageQueueRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88') // Storage Queue Data Contributor
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Storage Table Data Contributor — required for TokenStore, CrawlState, CatalogEntries tables
resource storageTableRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3') // Storage Table Data Contributor
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ====================================================================
// Outputs
// ====================================================================
output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output storageAccountName string = storage.name
output keyVaultName string = keyVault.name
output appInsightsName string = appInsights.name
