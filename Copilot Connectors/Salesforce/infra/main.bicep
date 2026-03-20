targetScope = 'resourceGroup'

@description('Primary location for all resources')
param location string = resourceGroup().location

@description('A unique suffix for resource names')
param resourceToken string = uniqueString(resourceGroup().id)

@description('Environment name (e.g., dev, prod)')
param environmentName string

// Salesforce connector secrets
@secure()
@description('Salesforce OAuth Client ID')
param sfClientId string

@secure()
@description('Salesforce OAuth Client Secret')
param sfClientSecret string

@secure()
@description('Salesforce Refresh Token')
param sfRefreshToken string

@description('Salesforce Instance URL')
param sfInstanceUrl string

@description('Salesforce API Version')
param sfApiVersion string = 'v66.0'

// Microsoft Graph / Entra connector settings
@description('Graph Connector ID')
param connectorId string = 'salesforceconnector'

@description('Graph Connector Display Name')
param connectorName string = 'Salesforce'

@description('Graph Connector Description')
param connectorDescription string = 'Salesforce CRM data for Microsoft 365 Copilot'

@secure()
@description('Entra App Client ID for Graph API')
param azureClientId string

@secure()
@description('Entra App Client Secret for Graph API')
param azureClientSecret string

@description('Entra Tenant ID')
param azureTenantId string

// Tags applied to all resources
var tags = {
  'azd-env-name': environmentName
}

// ── Log Analytics Workspace ──
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ── Application Insights ──
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ── Storage Account (required for Azure Functions) ──
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'stsfcon${resourceToken}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// ── App Service Plan (Basic B1 — avoids Consumption file share requirement) ──
resource hostingPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-${resourceToken}'
  location: location
  tags: tags
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true // Linux
  }
}

// ── Key Vault (for secrets) ──
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// Store secrets in Key Vault
resource secretSfClientId 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'sf-client-id'
  properties: {
    value: sfClientId
  }
}

resource secretSfClientSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'sf-client-secret'
  properties: {
    value: sfClientSecret
  }
}

resource secretSfRefreshToken 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'sf-refresh-token'
  properties: {
    value: sfRefreshToken
  }
}

resource secretAzureClientId 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'azure-client-id'
  properties: {
    value: azureClientId
  }
}

resource secretAzureClientSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'azure-client-secret'
  properties: {
    value: azureClientSecret
  }
}

// ── Function App ──
resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-sf-connector-${resourceToken}'
  location: location
  tags: union(tags, {
    'azd-service-name': 'api'
  })
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'Node|22'
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: storageAccount.name }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'node' }
        { name: 'FUNCTIONS_NODE_BLOCK_ON_ENTRY_POINT_ERROR', value: 'true' }
        { name: 'WEBSITE_NODE_DEFAULT_VERSION', value: '~22' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        // Salesforce settings
        { name: 'SF_INSTANCE_URL', value: sfInstanceUrl }
        { name: 'SF_API_VERSION', value: sfApiVersion }
        { name: 'SF_AUTH_FLOW', value: 'refresh_token' }
        { name: 'SF_CLIENT_ID', value: '@Microsoft.KeyVault(SecretUri=${secretSfClientId.properties.secretUri})' }
        { name: 'SF_CLIENT_SECRET', value: '@Microsoft.KeyVault(SecretUri=${secretSfClientSecret.properties.secretUri})' }
        { name: 'SF_REFRESH_TOKEN', value: '@Microsoft.KeyVault(SecretUri=${secretSfRefreshToken.properties.secretUri})' }
        // Graph / Entra settings
        { name: 'CONNECTOR_ID', value: connectorId }
        { name: 'CONNECTOR_NAME', value: connectorName }
        { name: 'CONNECTOR_DESCRIPTION', value: connectorDescription }
        { name: 'AZURE_TENANT_ID', value: azureTenantId }
        { name: 'AZURE_CLIENT_ID', value: '@Microsoft.KeyVault(SecretUri=${secretAzureClientId.properties.secretUri})' }
        { name: 'AZURE_CLIENT_SECRET', value: '@Microsoft.KeyVault(SecretUri=${secretAzureClientSecret.properties.secretUri})' }
      ]
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
  }
}

// Grant Function App access to Key Vault secrets (Key Vault Secrets User)
resource kvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionApp.id, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: keyVault
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalType: 'ServicePrincipal'
  }
}

// Grant Function App Storage Blob Data Owner on the storage account
resource storageBlobRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
  scope: storageAccount
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
    principalType: 'ServicePrincipal'
  }
}

// Grant Function App Storage Account Contributor (needed for queues/tables)
resource storageContribRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, '17d1049b-9a84-46fb-8f53-869881c3d3ab')
  scope: storageAccount
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '17d1049b-9a84-46fb-8f53-869881c3d3ab')
    principalType: 'ServicePrincipal'
  }
}

// Grant Function App Storage Queue Data Contributor (needed for timer triggers)
resource storageQueueRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
  scope: storageAccount
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
    principalType: 'ServicePrincipal'
  }
}

// Grant Function App Storage Table Data Contributor (needed for timer trigger schedule monitor)
resource storageTableRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
  scope: storageAccount
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
    principalType: 'ServicePrincipal'
  }
}

// ── Outputs ──
output AZURE_FUNCTION_NAME string = functionApp.name
output AZURE_FUNCTION_URL string = 'https://${functionApp.properties.defaultHostName}'
output AZURE_KEY_VAULT_NAME string = keyVault.name
output AZURE_APP_INSIGHTS_NAME string = appInsights.name
