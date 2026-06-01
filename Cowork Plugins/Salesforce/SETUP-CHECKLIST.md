# Salesforce Cowork Setup Checklist

Use this checklist to avoid value mix-ups across Entra, Teams Developer Portal, Azure, and the plugin manifest.

## 1) Identity and OAuth Inputs

- [ ] Tenant ID captured
- [ ] Salesforce OAuth client ID captured
- [ ] Salesforce OAuth client secret captured
- [ ] Salesforce authorize endpoint captured
- [ ] Salesforce token endpoint captured
- [ ] OAuth scope string captured

## 2) Teams Developer Portal OAuth Registration

- [ ] OAuth client registration created in Teams Developer Portal
- [ ] Base URL set to MCP host base URL
- [ ] Authorization endpoint set
- [ ] Token endpoint set
- [ ] Scope set
- [ ] OAuth registration ID captured (this is manifest `referenceId`)

## 3) Azure Deployment Inputs

- [ ] `AZURE_ENV_NAME` set
- [ ] `AZURE_LOCATION` set
- [ ] `DEPLOYER_PRINCIPAL_ID` set
- [ ] `SALESFORCE_BASE_URL` set (for example `https://mydomain.my.salesforce.com`)
- [ ] `SALESFORCE_API_VERSION` reviewed (`v61.0` default)

## 4) Deploy MCP Server

- [ ] `azd up` succeeds from the Salesforce plugin root
- [ ] `MCP_FULL_URL` output captured
- [ ] `MCP_FEDERATED_URL` output captured
- [ ] Health endpoints respond (`/health/live`, `/health/ready`)

## 5) Manifest Cutover

- [ ] `agentConnectors[0].toolSource.remoteMcpServer.mcpServerUrl` replaced with deployed `MCP_FULL_URL`
- [ ] `agentConnectors[0].toolSource.remoteMcpServer.authorization.referenceId` replaced with real OAuth registration ID
- [ ] Plugin `version` bumped for upload

## 6) Preflight Validation

- [ ] `./preflight.ps1` passes
- [ ] `./package.ps1 -SkipIcons` passes
- [ ] `./package.ps1` passes (when final icons are ready)

## 7) Upload and Connect

- [ ] Upload package in M365 Admin Center
- [ ] Publish to test users only
- [ ] Open fresh Cowork session
- [ ] Add plugin and run connection prompt

## 8) Smoke Test Prompts

- [ ] Account briefing prompt
- [ ] Opportunity health prompt
- [ ] Next-best-action prompt
- [ ] Update opportunity prompt (confirmation expected)
- [ ] Log call notes prompt (confirmation expected)

## 9) Common Failure Checks

- [ ] OAuth registration ID is not confused with OAuth client ID
- [ ] MCP URL includes `/mcp/full`
- [ ] Plugin version changed before re-upload
- [ ] Test user has Salesforce object access required for queried data
