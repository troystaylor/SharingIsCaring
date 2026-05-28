# Planner Azure Infrastructure

This folder contains Bicep for deploying the Planner MCP server to Azure Container Apps with:

- Resource group
- Log Analytics + Application Insights
- Container Apps environment
- Container App + ACR
- Required RBAC assignments for deployer and managed identity

## Parameters

- `environmentName`
- `location`
- `deployerPrincipalId`

## Outputs

- `AZURE_RESOURCE_GROUP`
- `AZURE_CONTAINER_APP_FQDN`
- `MCP_FULL_URL`
- `MCP_FEDERATED_URL`
