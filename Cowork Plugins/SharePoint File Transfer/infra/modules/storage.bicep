param location string
param resourceToken string
param tags object

@description('Name of the Azure Table that stores upload session metadata.')
param tableName string = 'uploadSessions'

var storageName = toLower('stmcp${resourceToken}')

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Enabled'
    supportsHttpsTrafficOnly: true
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource uploadSessionsTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: tableName
}

output storageAccountId string = storage.id
output storageAccountName string = storage.name
output tableEndpoint string = storage.properties.primaryEndpoints.table
output tableName string = uploadSessionsTable.name
