// main.bicep — ACA Code Interpreter Infrastructure
// Deploys: Resource Group (if needed), Sandbox Group, RBAC role assignment
//
// Usage:
//   az deployment sub create \
//     --location eastus2 \
//     --template-file main.bicep \
//     --parameters principalId=<your-user-or-sp-object-id>

targetScope = 'subscription'

@description('Azure region for the sandbox group')
param location string = 'eastus2'

@description('Name of the resource group')
param resourceGroupName string = 'rg-aca-code-interpreter'

@description('Name of the sandbox group')
param sandboxGroupName string = 'code-interpreter'

@description('Principal ID (user or service principal) to grant data-plane access')
param principalId string

@description('Principal type: User, Group, or ServicePrincipal')
@allowed(['User', 'Group', 'ServicePrincipal'])
param principalType string = 'User'

@description('Enable system-assigned managed identity on the sandbox group')
param enableSystemIdentity bool = true

// ─── Resource Group ───────────────────────────────────────────────────────────
resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: {
    purpose: 'aca-code-interpreter'
    managedBy: 'bicep'
  }
}

// ─── Sandbox Group + RBAC ─────────────────────────────────────────────────────
module sandboxGroup 'modules/sandbox-group.bicep' = {
  name: 'deploy-sandbox-group'
  scope: rg
  params: {
    location: location
    sandboxGroupName: sandboxGroupName
    principalId: principalId
    principalType: principalType
    enableSystemIdentity: enableSystemIdentity
  }
}

// ─── Outputs ──────────────────────────────────────────────────────────────────
output resourceGroupId string = rg.id
output sandboxGroupId string = sandboxGroup.outputs.sandboxGroupId
output sandboxGroupIdentityPrincipalId string = sandboxGroup.outputs.identityPrincipalId
output dataPlaneEndpoint string = 'https://${location}.data.sandboxes.azure.com'
output connectorConstants object = {
  ACA_SUBSCRIPTION_ID: subscription().subscriptionId
  ACA_RESOURCE_GROUP: resourceGroupName
  ACA_SANDBOX_GROUP: sandboxGroupName
  ACA_REGION: location
}
