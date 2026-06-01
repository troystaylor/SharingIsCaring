# NetSuite Cowork Setup Checklist

Use this checklist to avoid value mix-ups across NetSuite, Teams Developer Portal, Azure, and the plugin manifest.

## 1) NetSuite OAuth Inputs

- [ ] NetSuite Account ID captured (e.g. `1234567` or `TSTDRV1234567_SB1`)
- [ ] NetSuite OAuth Client ID captured (from Integration Record)
- [ ] NetSuite OAuth Client Secret captured (shown once on save)
- [ ] OAuth scope `rest_webservices` enabled on the Integration Record
- [ ] Authorization endpoint resolved: `https://<account>.app.netsuite.com/app/login/oauth2/authorize.nl`
- [ ] Token endpoint resolved: `https://<account>.suitetalk.api.netsuite.com/services/rest/auth/oauth2/v1/token`

## 2) Teams Developer Portal OAuth Registration

- [ ] OAuth client registration created in Teams Developer Portal (`dev.teams.cloud.microsoft/tools`)
- [ ] Authorization endpoint set
- [ ] Token endpoint set
- [ ] Scope set to `rest_webservices`
- [ ] Client ID and Client Secret pasted from NetSuite Integration Record
- [ ] OAuth registration ID captured (this is manifest `referenceId`)

## 3) Azure Deployment Inputs

- [ ] `AZURE_ENV_NAME` set (e.g. `netsuite-dev`)
- [ ] `AZURE_LOCATION` set (e.g. `westus2`)
- [ ] `DEPLOYER_PRINCIPAL_ID` set (object id of the user running `azd up`)
- [ ] `NETSUITE_ACCOUNT_ID` set (your NetSuite account id)

## 4) Deploy MCP Server

- [ ] `azd up` succeeds from the NetSuite plugin root
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
- [ ] Add plugin and complete OAuth connection prompt

## 8) Smoke Test Prompts

- [ ] Customer briefing prompt
- [ ] Open sales orders prompt
- [ ] AR aging prompt
- [ ] SuiteQL ad-hoc query prompt
- [ ] Create/update record prompt (confirmation expected)

## 9) Common Failure Checks

- [ ] OAuth registration ID is not confused with NetSuite Client ID
- [ ] MCP URL includes `/mcp/full`
- [ ] Plugin version changed before re-upload
- [ ] Test user has NetSuite role with REST Web Services + record permissions
- [ ] Account ID uses underscores, not hyphens, in the subdomain
