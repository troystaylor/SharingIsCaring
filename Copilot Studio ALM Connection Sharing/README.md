# Copilot Studio ALM: share connections with Power Virtual Agents Service

When you move a Copilot Studio agent between environments with a solution, the agent can stop working in the target environment even though the solution imported cleanly. This folder contains an Azure DevOps pipeline and script that fix it by sharing the connection with the **Power Virtual Agents Service** after each import.

## The problem

Copilot Studio agents call connector tools (for example the certified Databricks connector) through a Microsoft first-party service identity named **Power Virtual Agents Service**. That identity needs **Can use** on the connection.

When you create or select a connection in Copilot Studio, it is shared with Power Virtual Agents Service for you. A solution import only maps the connection reference to a connection in the target environment. It doesn't share that connection with the service, so the agent fails at runtime until you share it manually.

> Microsoft doesn't document this behavior. The explanation above is based on observed behavior, so confirm it in your own environment.

## What's in this folder

| File | Purpose |
|------|---------|
| `azure-pipelines.yml` | Imports the agent solution, shares the connection, then publishes customizations |
| `Share-ConnectionWithPVAS.ps1` | Looks up Power Virtual Agents Service in your tenant and grants it access to a connection |
| `settings/test.json`, `settings/prod.json` | Deployment settings that map the connection reference to the connection in each environment |

## Prerequisites

- The connection already exists in each target environment. Create it with a service account or service principal where the connector supports it, so it doesn't depend on one person.
- Power Platform Build Tools installed in your Azure DevOps organization.
- A Power Platform service connection and an Azure service connection in Azure DevOps.
- The identity behind the Azure service connection can read service principals in Microsoft Entra ID (`Application.Read.All`) and administer connections in the target environment.

## Setup

1. Copy the files into your repository. Keep `Share-ConnectionWithPVAS.ps1` at the repository root, or change `scriptPath` in the pipeline.
2. Edit `settings/test.json` and `settings/prod.json`. Set `LogicalName` to your connection reference's logical name and `ConnectionId` to the target environment's connection ID.
3. Create a variable group per environment, named `alm-test` and `alm-prod`:

   | Variable | Value |
   |----------|-------|
   | `PowerPlatformServiceConnection` | Name of your Power Platform service connection |
   | `AzureServiceConnection` | Name of your Azure service connection |
   | `PowerPlatformEnvironmentId` | Environment ID (GUID) |
   | `ConnectorName` | `shared_databricks` (confirm in your environment) |
   | `ConnectionName` | Connection ID that the connection reference points to |

4. Point `SolutionInputFile` in the pipeline at your managed solution artifact.
5. Confirm the principal payload before the first real run. In make.powerapps.com, share a connection with Power Virtual Agents Service while capturing network traffic in your browser's developer tools. Compare the `modifyPermissions` request body to the one in `Share-ConnectionWithPVAS.ps1`, and adjust the principal type if it differs.
6. Run the pipeline.

## Verify

Open the connection in make.powerapps.com, select **Share**, and confirm **Power Virtual Agents Service** is listed with **Can use**. Then test the agent.

## Notes

- The service principal's object ID is different in every tenant. The script looks it up at run time. It's the same across environments in one tenant.
- The application (client) ID is the same in every tenant.
- Look it up manually with:

  ```powershell
  az ad sp list --display-name "Power Virtual Agents Service" --query "[].{name:displayName,appId:appId,objectId:id}" -o table
  ```

## Manual alternatives

- **Share by hand:** In make.powerapps.com, go to **Connections**, select the connection, choose **Share**, and add **Power Virtual Agents Service** with **Can use**.
- **Rebind in Copilot Studio:** Open the agent, go to **Settings > Connection settings**, reselect the connection, submit it, and publish the agent.

## Troubleshooting

| Symptom | Likely cause and fix |
|---------|----------------------|
| Agent works in dev but the connector fails in test or prod | The connection isn't shared with Power Virtual Agents Service. Run the script or share it manually. |
| `Power Virtual Agents Service was not found in this tenant` | The tenant hasn't used Copilot Studio yet, or the identity can't read service principals. Check `Application.Read.All`. |
| `modifyPermissions` returns 400 | The principal type or role in the request body is wrong. Capture the request from the UI and match it. |
| `modifyPermissions` returns 403 | The pipeline identity isn't a Power Platform admin and doesn't own the connection. |
| Connection isn't offered when you reselect it in Copilot Studio | You need **Can use + share** on the connection. Ask the owner to share it. |
