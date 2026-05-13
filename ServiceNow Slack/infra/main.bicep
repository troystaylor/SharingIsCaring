// ── Bicep: ServiceNow Slack Copilot Connector Infrastructure ──

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Unique token for resource naming')
param resourceToken string = uniqueString(resourceGroup().id)

@description('Environment name')
param environmentName string = 'servicenow-slack-connector'

// ── ServiceNow credentials ──
@secure()
param snClientId string
@secure()
param snClientSecret string
param snInstanceUrl string
param snAuthFlow string = 'client_credentials'
param snSlackIndexedTable string = 'u_slack_content'

// ── Entra / Graph credentials ──
@secure()
param azureClientId string
@secure()
param azureClientSecret string
param azureTenantId string = subscription().tenantId

// ── Connector metadata ──
param connectorId string = 'servicenow-slack'
param connectorName string = 'ServiceNow Slack'
param connectorDescription string = 'Slack messages and attachments from public channels indexed by ServiceNow AI Search'

// ── Log Analytics ──
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'log-${resourceToken}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ── Application Insights ──
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'ai-${resourceToken}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ── Storage Account ──
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: 'st${resourceToken}'
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// ── App Service Plan (Consumption) ──
resource plan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: 'plan-${resourceToken}'
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: true
  }
}

// ── Key Vault ──
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${resourceToken}'
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
  }
}

// ── Key Vault Secrets ──
resource secretSnClientId 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'sn-client-id'
  properties: { value: snClientId }
}

resource secretSnClientSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'sn-client-secret'
  properties: { value: snClientSecret }
}

resource secretAzureClientId 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'azure-client-id'
  properties: { value: azureClientId }
}

resource secretAzureClientSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'azure-client-secret'
  properties: { value: azureClientSecret }
}

// ── Function App ──
resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: 'func-${resourceToken}'
  location: location
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'Node|20'
      appSettings: [
        { name: 'AzureWebJobsStorage__blobServiceUri', value: 'https://${storage.name}.blob.${environment().suffixes.storage}' }
        { name: 'AzureWebJobsStorage__queueServiceUri', value: 'https://${storage.name}.queue.${environment().suffixes.storage}' }
        { name: 'AzureWebJobsStorage__tableServiceUri', value: 'https://${storage.name}.table.${environment().suffixes.storage}' }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'node' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'SN_INSTANCE_URL', value: snInstanceUrl }
        { name: 'SN_CLIENT_ID', value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=sn-client-id)' }
        { name: 'SN_CLIENT_SECRET', value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=sn-client-secret)' }
        { name: 'SN_AUTH_FLOW', value: snAuthFlow }
        { name: 'SN_SLACK_INDEXED_TABLE', value: snSlackIndexedTable }
        { name: 'AZURE_TENANT_ID', value: azureTenantId }
        { name: 'AZURE_CLIENT_ID', value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=azure-client-id)' }
        { name: 'AZURE_CLIENT_SECRET', value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=azure-client-secret)' }
        { name: 'CONNECTOR_ID', value: connectorId }
        { name: 'CONNECTOR_NAME', value: connectorName }
        { name: 'CONNECTOR_DESCRIPTION', value: connectorDescription }
      ]
    }
  }
}

// ── RBAC: Function App → Storage (Blob Data Owner + Queue Data Contributor + Table Data Contributor) ──
resource storageBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageQueueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── RBAC: Function App → Key Vault Secrets User ──
resource kvSecretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionApp.id, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
