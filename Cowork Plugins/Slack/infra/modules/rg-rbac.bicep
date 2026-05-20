// Grants the deployer Contributor on the resource group. Done in a module so
// the RG-scoped role assignment runs at RG scope (the parent main.bicep is
// subscription-scoped).

param deployerPrincipalId string

resource rgContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deployerPrincipalId, 'rg-contributor')
  properties: {
    principalId: deployerPrincipalId
    // Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
    principalType: 'User'
  }
}
