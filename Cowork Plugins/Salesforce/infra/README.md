# Salesforce Plugin Infrastructure

Infrastructure scaffold for deploying the Salesforce MCP server to Azure using azd + Bicep.

## Included resources

- Resource group (subscription-scoped orchestration)
- Log Analytics workspace
- Application Insights (workspace-based)
- Azure Container Apps environment
- Azure Container Registry
- Container App for the MCP server

## Quick start

1. Set required environment variables:
	- `AZURE_ENV_NAME`
	- `AZURE_LOCATION`
	- `DEPLOYER_PRINCIPAL_ID`
	- `SALESFORCE_BASE_URL`
2. Optional variable:
	- `SALESFORCE_API_VERSION` (defaults to `v61.0`)
3. Deploy:

```powershell
cd "Cowork Plugins/Salesforce"
azd up
```

## Outputs

- `MCP_FULL_URL`
- `MCP_FEDERATED_URL`
- `AZURE_CONTAINER_APP_FQDN`
- `AZURE_RESOURCE_GROUP`

Use `MCP_FULL_URL` in the Salesforce plugin manifest connector after deployment.
