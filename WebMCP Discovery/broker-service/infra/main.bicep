@description('Name of the Container App Environment')
param environmentName string = 'webmcp-env'

@description('Name of the Container App')
param containerAppName string = 'webmcp-broker'

@description('Location for all resources')
param location string = resourceGroup().location

@description('Container image to deploy')
param containerImage string = 'ghcr.io/troystaylor/sharingiscaring/webmcp-broker:latest'

@description('API Key for broker authentication')
@secure()
param apiKey string

@description('Maximum number of replicas')
param maxReplicas int = 5

@description('Minimum number of replicas (0 for scale-to-zero)')
param minReplicas int = 0

// --- Security & Compliance Parameters ---

@description('Authentication mode: apikey, managed-identity, or both')
@allowed(['apikey', 'managed-identity', 'both'])
param authMode string = 'apikey'

@description('Azure AD tenant ID for managed identity auth')
param azureTenantId string = ''

@description('Azure AD client ID (audience) for token validation')
param azureClientId string = ''

@description('API key-to-role mapping as JSON (e.g., {"key1":"admin","key2":"viewer"})')
@secure()
param apiKeys string = '{}'

@description('Enable RBAC (role-based access control)')
param rbacEnabled bool = false

@description('Comma-separated list of allowed domains (empty = allow all)')
param allowedDomains string = ''

@description('Comma-separated list of blocked domains')
param blockedDomains string = ''

@description('Enable network egress control at browser level')
param networkEgressControl bool = true

@description('Audit log level: none, basic, detailed, full')
@allowed(['none', 'basic', 'detailed', 'full'])
param auditLogLevel string = 'basic'

@description('Azure Monitor custom endpoint for audit log ingestion')
param azureMonitorEndpoint string = ''

@description('Enable session action recording')
param sessionRecording bool = false

@description('Comma-separated custom redaction regex patterns')
param redactionPatterns string = ''

@description('Comma-separated field names to redact')
param redactionFields string = 'password,ssn,credit_card,api_key,secret,token,authorization'

@description('Deploy with VNet integration for private networking')
param enableVnet bool = false

@description('VNet name (created if enableVnet is true)')
param vnetName string = 'webmcp-vnet'

@description('VNet address prefix')
param vnetAddressPrefix string = '10.0.0.0/16'

@description('Container App subnet prefix')
param containerSubnetPrefix string = '10.0.0.0/23'

@description('Private endpoint subnet prefix')
param privateEndpointSubnetPrefix string = '10.0.2.0/24'

// Log Analytics Workspace
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${environmentName}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// --- Optional VNet for private networking ---
resource vnet 'Microsoft.Network/virtualNetworks@2023-05-01' = if (enableVnet) {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [vnetAddressPrefix]
    }
    subnets: [
      {
        name: 'container-apps'
        properties: {
          addressPrefix: containerSubnetPrefix
          delegations: [
            {
              name: 'Microsoft.App.environments'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
        }
      }
    ]
  }
}

// Container App Environment
resource containerAppEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: enableVnet ? {
      infrastructureSubnetId: vnet.properties.subnets[0].id
      internal: true
    } : null
  }
}

// Container App
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  properties: {
    managedEnvironmentId: containerAppEnv.id
    configuration: {
      ingress: {
        external: !enableVnet
        targetPort: 3000
        transport: 'http'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      secrets: [
        {
          name: 'api-key'
          value: apiKey
        }
        {
          name: 'api-keys-json'
          value: apiKeys
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'webmcp-broker'
          image: containerImage
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
          }
          env: [
            // Core
            {
              name: 'API_KEY'
              secretRef: 'api-key'
            }
            {
              name: 'PORT'
              value: '3000'
            }
            {
              name: 'MAX_BROWSERS'
              value: '5'
            }
            // Authentication
            {
              name: 'AUTH_MODE'
              value: authMode
            }
            {
              name: 'AZURE_TENANT_ID'
              value: azureTenantId
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: azureClientId
            }
            {
              name: 'API_KEYS'
              secretRef: 'api-keys-json'
            }
            // RBAC
            {
              name: 'RBAC_ENABLED'
              value: string(rbacEnabled)
            }
            // URL Allowlisting
            {
              name: 'ALLOWED_DOMAINS'
              value: allowedDomains
            }
            {
              name: 'BLOCKED_DOMAINS'
              value: blockedDomains
            }
            {
              name: 'NETWORK_EGRESS_CONTROL'
              value: string(networkEgressControl)
            }
            // Audit Logging
            {
              name: 'AUDIT_LOG_LEVEL'
              value: auditLogLevel
            }
            {
              name: 'AZURE_MONITOR_ENDPOINT'
              value: azureMonitorEndpoint
            }
            // Session Recording
            {
              name: 'SESSION_RECORDING'
              value: string(sessionRecording)
            }
            // Data Redaction
            {
              name: 'REDACTION_PATTERNS'
              value: redactionPatterns
            }
            {
              name: 'REDACTION_FIELDS'
              value: redactionFields
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 3000
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 3000
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '10'
              }
            }
          }
        ]
      }
    }
  }
}

// --- Private DNS Zone (for VNet-internal access) ---
resource privateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = if (enableVnet) {
  name: '${environmentName}.internal.azurecontainerapps.io'
  location: 'global'
}

resource privateDnsVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = if (enableVnet) {
  parent: privateDnsZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
}

output fqdn string = containerApp.properties.configuration.ingress.fqdn
output url string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output vnetId string = enableVnet ? vnet.id : ''
