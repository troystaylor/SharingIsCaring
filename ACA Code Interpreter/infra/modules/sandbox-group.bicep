// modules/sandbox-group.bicep — ACA Sandbox Group + RBAC
// Deploys the sandbox group resource and grants data-plane access

@description('Azure region')
param location string

@description('Sandbox group name')
param sandboxGroupName string

@description('Principal ID to grant data-plane access')
param principalId string

@description('Principal type')
@allowed(['User', 'Group', 'ServicePrincipal'])
param principalType string

@description('Enable system-assigned identity')
param enableSystemIdentity bool

// Container Apps SandboxGroup Data Owner — built-in role
var sandboxDataOwnerRoleId = 'c24cf47c-5077-412d-a19c-45202126392c'

// ─── Sandbox Group ────────────────────────────────────────────────────────────
resource sandboxGroup 'Microsoft.App/sandboxGroups@2026-02-01-preview' = {
  name: sandboxGroupName
  location: location
  identity: enableSystemIdentity ? {
    type: 'SystemAssigned'
  } : null
  properties: {}
  tags: {
    connector: 'aca-code-interpreter'
    managedBy: 'bicep'
  }
}

// ─── RBAC: Grant data-plane access ────────────────────────────────────────────
resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(sandboxGroup.id, principalId, sandboxDataOwnerRoleId)
  scope: sandboxGroup
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', sandboxDataOwnerRoleId)
    principalId: principalId
    principalType: principalType
  }
}

// ─── Outputs ──────────────────────────────────────────────────────────────────
output sandboxGroupId string = sandboxGroup.id
output sandboxGroupName string = sandboxGroup.name
output identityPrincipalId string = enableSystemIdentity ? sandboxGroup.identity.principalId : ''
