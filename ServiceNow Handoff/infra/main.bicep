// ============================================================================
// Azure Bicep — ServiceNow Handoff Agent Infrastructure
// ============================================================================
// Deploys:
//   - App Service Plan (Linux, B1)
//   - App Service (Web App for the .NET agent)
//   - Azure Bot resource (connected to the App Service)
//   - Application Insights + Log Analytics workspace
//   - Managed Identity for the App Service
//
// USAGE:
//   az login
//   az group create --name rg-servicenow-handoff --location eastus
//   az deployment group create \
//     --resource-group rg-servicenow-handoff \
//     --template-file infra/main.bicep \
//     --parameters botAppId=YOUR-BOT-APP-ID botAppSecret=YOUR-BOT-APP-SECRET
//
// After deployment:
//   1. Deploy your code: az webapp deploy --resource-group rg-servicenow-handoff --name <appName> --src-path publish.zip
//   2. Set remaining app settings (Copilot Studio, ServiceNow) in the Azure Portal or via CLI
//   3. Update the Bot's messaging endpoint if needed
// ============================================================================

targetScope = 'resourceGroup'

@description('Base name for all resources (lowercase, no spaces)')
param baseName string = 'snhandoff'

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('App Service Plan SKU')
param appServicePlanSku string = 'B1'

@description('Azure Bot App Registration Client ID (from Microsoft Entra ID)')
@secure()
param botAppId string

@description('Azure Bot App Registration Client Secret')
@secure()
param botAppSecret string

@description('Copilot Studio Direct Connect URL (from Copilot Studio > Channels > Web app)')
param copilotStudioDirectConnectUrl string = ''

@description('ServiceNow instance URL (e.g., https://myinstance.service-now.com)')
param serviceNowInstanceUrl string = ''

@description('ServiceNow OAuth Client ID')
@secure()
param serviceNowClientId string = ''

@description('ServiceNow OAuth Client Secret')
@secure()
param serviceNowClientSecret string = ''

@description('ServiceNow webhook secret for HMAC validation')
@secure()
param serviceNowWebhookSecret string = ''

@description('Microsoft Entra Tenant ID')
param tenantId string = subscription().tenantId

// ---- Unique name suffix ----
var uniqueSuffix = uniqueString(resourceGroup().id)
var appName = '${baseName}-${uniqueSuffix}'

// ---- Log Analytics Workspace ----
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${baseName}-logs-${uniqueSuffix}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ---- Application Insights ----
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${baseName}-insights-${uniqueSuffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---- App Service Plan ----
resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${baseName}-plan-${uniqueSuffix}'
  location: location
  sku: {
    name: appServicePlanSku
  }
  kind: 'linux'
  properties: {
    reserved: true // Required for Linux
  }
}

// ---- App Service (Web App) ----
resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true // Required for WebSocket and background services
      webSocketsEnabled: true // Required for Direct Line WebSocket fallback
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'Connections__BotServiceConnection__Assembly'
          value: 'Microsoft.Agents.Authentication.Msal'
        }
        {
          name: 'Connections__BotServiceConnection__Type'
          value: 'MsalAuth'
        }
        {
          name: 'Connections__BotServiceConnection__Settings__AuthType'
          value: 'ClientSecret'
        }
        {
          name: 'Connections__BotServiceConnection__Settings__AuthorityEndpoint'
          value: 'https://login.microsoftonline.com/${tenantId}'
        }
        {
          name: 'Connections__BotServiceConnection__Settings__ClientId'
          value: botAppId
        }
        {
          name: 'Connections__BotServiceConnection__Settings__ClientSecret'
          value: botAppSecret
        }
        {
          name: 'Connections__BotServiceConnection__Settings__TenantId'
          value: tenantId
        }
        {
          name: 'CopilotStudioClientSettings__DirectConnectUrl'
          value: copilotStudioDirectConnectUrl
        }
        {
          name: 'ServiceNow__InstanceUrl'
          value: serviceNowInstanceUrl
        }
        {
          name: 'ServiceNow__ClientId'
          value: serviceNowClientId
        }
        {
          name: 'ServiceNow__ClientSecret'
          value: serviceNowClientSecret
        }
        {
          name: 'ServiceNow__WebhookSecret'
          value: serviceNowWebhookSecret
        }
      ]
    }
  }
}

// ---- Azure Bot ----
resource azureBot 'Microsoft.BotService/botServices@2022-09-15' = {
  name: '${baseName}-bot-${uniqueSuffix}'
  location: 'global' // Bot Service is always global
  kind: 'azurebot'
  sku: {
    name: 'S1'
  }
  properties: {
    displayName: 'ServiceNow Handoff Agent'
    description: 'Copilot Studio agent with ServiceNow live agent handoff'
    endpoint: 'https://${webApp.properties.defaultHostName}/api/messages'
    msaAppId: botAppId
    msaAppType: 'SingleTenant'
    msaAppTenantId: tenantId
  }
}

// ---- Enable Web Chat channel on the Bot ----
resource webChatChannel 'Microsoft.BotService/botServices/channels@2022-09-15' = {
  parent: azureBot
  name: 'WebChatChannel'
  location: 'global'
  properties: {
    channelName: 'WebChatChannel'
  }
}

// ---- Outputs ----
output appServiceName string = webApp.name
output appServiceUrl string = 'https://${webApp.properties.defaultHostName}'
output botMessagingEndpoint string = 'https://${webApp.properties.defaultHostName}/api/messages'
output serviceNowWebhookUrl string = 'https://${webApp.properties.defaultHostName}/api/servicenow/webhook'
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output botName string = azureBot.name
