# NetSuite Plugin Infrastructure

Infrastructure scaffold for deploying the NetSuite MCP server to Azure using azd + Bicep.

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
	- `NETSUITE_ACCOUNT_ID` (e.g. `1234567` or `TSTDRV1234567_SB1`)
2. Deploy:

```powershell
cd "Cowork Plugins/NetSuite"
azd up
```

## Outputs

- `MCP_FULL_URL`
- `MCP_FEDERATED_URL`
- `AZURE_CONTAINER_APP_FQDN`
- `AZURE_RESOURCE_GROUP`

Use `MCP_FULL_URL` in the NetSuite plugin manifest connector after deployment.
