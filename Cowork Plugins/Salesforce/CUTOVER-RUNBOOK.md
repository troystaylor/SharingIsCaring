# Salesforce Manifest and Auth Cutover Runbook

This runbook is for switching from placeholders to real deployment/auth values.

## Inputs Required

- Deployed MCP URL (`MCP_FULL_URL`)
- OAuth registration ID (Teams Developer Portal)
- New plugin version (for example `0.1.1`)

## Step-by-Step

1. Confirm deployment outputs:
   - `MCP_FULL_URL`
   - `MCP_FEDERATED_URL`
2. Open [manifest.json](manifest.json).
3. Update connector endpoint:
   - `agentConnectors[0].toolSource.remoteMcpServer.mcpServerUrl = <MCP_FULL_URL>`
4. Update connector OAuth binding:
   - `agentConnectors[0].toolSource.remoteMcpServer.authorization.referenceId = <OAuth registration ID>`
5. Bump plugin `version`.
6. Run preflight:
   - `./preflight.ps1`
7. Build package:
   - `./package.ps1 -SkipIcons`
8. Upload in M365 Admin Center and test in a fresh Cowork session.

## Validation Commands

```powershell
cd "Cowork Plugins/Salesforce"
./preflight.ps1
./package.ps1 -SkipIcons
```

## Rollback

1. Re-upload previous known-good package version.
2. Revert manifest connector URL and `referenceId` to previous values in source control if needed.
3. Re-test with the previous plugin package in a fresh Cowork session.
