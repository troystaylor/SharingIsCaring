param location string
param resourceToken string
param tags object

@secure()
param slackClientSecret string

@secure()
param coworkClientSecret string

@description('Object id of the deployer; granted Key Vault Administrator so they can rotate secrets.')
param deployerPrincipalId string

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${resourceToken}'
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// Slack client secret stored in Key Vault; surfaced to Container App via secretRef.
resource slackSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kv
  name: 'slack-client-secret'
  properties: {
    value: slackClientSecret
    attributes: { enabled: true }
  }
}

// Cowork-facing OAuth client secret (verified at /oauth/token).
resource coworkSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kv
  name: 'cowork-client-secret'
  properties: {
    value: coworkClientSecret
    attributes: { enabled: true }
  }
}

// Key Vault Administrator role for the deployer (rotation, audit, etc.)
resource kvAdminAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, deployerPrincipalId, 'kv-admin')
  scope: kv
  properties: {
    principalId: deployerPrincipalId
    // Key Vault Administrator
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '00482a5a-887f-4fb3-b363-3b7fe8e74483')
    principalType: 'User'
  }
}

output id string = kv.id
output name string = kv.name
output uri string = kv.properties.vaultUri
output slackSecretName string = slackSecret.name
output coworkSecretName string = coworkSecret.name
