@description('Name of the App Service')
param appServiceName string = 'pw-workspaces-broker'

@description('Name of the App Service Plan')
param appServicePlanName string = 'pw-workspaces-plan'

@description('Location for all resources')
param location string = resourceGroup().location

@description('App Service Plan SKU')
@allowed(['B1', 'B2', 'S1', 'S2', 'P1v3', 'P2v3'])
param skuName string = 'B2'

@description('API Key for broker authentication')
@secure()
param apiKey string

@description('Playwright Workspaces service URL (wss://...) — leave empty for local Chromium mode')
@secure()
param playwrightServiceUrl string = ''

@description('Playwright Workspaces access token — leave empty for local Chromium mode')
@secure()
param playwrightAccessToken string = ''

@description('Maximum concurrent browser sessions')
param maxSessions int = 10

@description('Default session TTL in minutes')
param defaultSessionTtl int = 15

@description('Default remote browser OS')
@allowed(['linux', 'windows'])
param defaultBrowserOs string = 'linux'

// --- App Service Plan ---
resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  sku: {
    name: skuName
  }
  properties: {
    reserved: true // Linux
  }
}

// --- App Service ---
resource appService 'Microsoft.Web/sites@2023-12-01' = {
  name: appServiceName
  location: location
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'NODE|20-lts'
      appCommandLine: 'bash startup.sh'
      alwaysOn: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      webSocketsEnabled: true // Needed for CDP WebSocket connections
      appSettings: [
        {
          name: 'API_KEY'
          value: apiKey
        }
        {
          name: 'PLAYWRIGHT_SERVICE_URL'
          value: playwrightServiceUrl
        }
        {
          name: 'PLAYWRIGHT_SERVICE_ACCESS_TOKEN'
          value: playwrightAccessToken
        }
        {
          name: 'MAX_SESSIONS'
          value: string(maxSessions)
        }
        {
          name: 'DEFAULT_SESSION_TTL_MINUTES'
          value: string(defaultSessionTtl)
        }
        {
          name: 'DEFAULT_BROWSER_OS'
          value: defaultBrowserOs
        }
        {
          name: 'NODE_ENV'
          value: 'production'
        }
        {
          name: 'WEBSITE_NODE_DEFAULT_VERSION'
          value: '~20'
        }
      ]
    }
  }
}

output appServiceUrl string = 'https://${appService.properties.defaultHostName}'
output appServiceName string = appService.name
