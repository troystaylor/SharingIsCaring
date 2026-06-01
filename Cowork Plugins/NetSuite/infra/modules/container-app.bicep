param location string
param resourceToken string
param tags object
param environmentId string

@secure()
param appInsightsConnectionString string

param deployerPrincipalId string

@minLength(1)
param netsuiteAccountId string

var acrName = toLower('acrmcp${resourceToken}')

// Convert hyphens to underscores in subdomain form (NetSuite sandbox convention)
var subdomainAccount = replace(netsuiteAccountId, '-', '_')
var netsuiteBaseUrl = 'https://${subdomainAccount}.suitetalk.api.netsuite.com/services/rest'

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

var initialImage = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-${resourceToken}'
  location: location
  tags: union(tags, { 'azd-service-name': 'mcp' })
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: []
      secrets: [
        {
          name: 'appinsights-connectionstring'
          value: appInsightsConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'mcp'
          image: initialImage
          resources: {
            cpu: json('0.5')
            memory: '1.0Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              secretRef: 'appinsights-connectionstring'
            }
            {
              name: 'NetSuite__BaseUrl'
              value: netsuiteBaseUrl
            }
            {
              name: 'NetSuite__AccountId'
              value: netsuiteAccountId
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http-rule'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, app.id, 'acrpull')
  scope: acr
  properties: {
    principalId: app.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalType: 'ServicePrincipal'
  }
}

resource webContrib 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(app.id, deployerPrincipalId, 'web-contrib')
  scope: app
  properties: {
    principalId: deployerPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'de139f84-1756-47ae-9be6-808fbbe84772')
    principalType: 'User'
  }
}

output containerAppId string = app.id
output principalId string = app.identity.principalId
output fqdn string = app.properties.configuration.ingress.fqdn
output acrLoginServer string = acr.properties.loginServer
output acrName string = acr.name
