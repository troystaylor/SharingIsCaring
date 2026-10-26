# Copilot Studio MCP Server

A gateway that lets one Microsoft 365 Copilot plugin use the [Copilot Studio MCP server](https://github.com/mcp/com.microsoft/copilot-studio) in **any Power Platform environment the signed-in user can access**.

Microsoft hosts the Copilot Studio MCP server once per environment, at a URL built from the environment ID, and a plugin manifest can only point at one fixed URL. This gateway gives the plugin that one URL. It adds a required `environmentId` argument to every Copilot Studio tool, adds a `list_environments` tool, and forwards each call to the environment the model chose.

It is not a rewrite of Copilot Studio's tools. It reads the tool list from Copilot Studio when it needs it, so new tools and schema changes appear without a redeploy.

## How it works

```
Microsoft 365 Copilot ──► this gateway (/mcp) ──► Microsoft Entra ──► https://<environment>.environment.api.powerplatform.com/...
   plugin, signed in as       checks the token,     swaps it for a        Copilot Studio, running as
   the user, for this         picks the             Power Platform        the same user
   gateway's own API          environment           token for that user
                              from environmentId    (on-behalf-of)
```

| Tool | What it does |
|------|--------------|
| `list_environments` | Lists the environments the user can access (from the Power Platform environment-management API) |
| `get_copilot_studio_skills`, `manage_agents`, `manage_workflows`, `discover_resources`, `get_consumption` | Copilot Studio's own tools, each with `environmentId` added as the first required argument |

## Sign-in and trust

**Each person signs in as themselves.** The plugin signs the user in to this gateway's own Entra API (scope `access_as_user`) and sends that access token as a bearer token. The gateway does three things with it:

1. **Checks it.** The signature, tenant, audience (this gateway's app), lifetime, and the `access_as_user` scope. A request from another tenant, for another audience, or without the scope is refused before anything else happens. A token issued for Power Platform is refused too.
2. **Exchanges it.** It trades that token for a Power Platform token for the same user (the OAuth on-behalf-of flow). Entra only does this when the app has consent for the Power Platform permissions, so consent is the control point.
3. **Calls Copilot Studio with the exchanged token.** Copilot Studio and Power Platform permissions decide what the user can do.

Why not just forward the plugin's token? A token issued for Power Platform carries a `nonce` in its header, which means only Power Platform can verify its signature. A gateway cannot check it, so it would have to trust the caller. Issuing the token for the gateway itself avoids that.

Things worth knowing:

- **No secret to store.** The gateway proves it is the Entra app with a token from its user-assigned managed identity, which the app trusts through a federated credential (created by `deploy.ps1`). No client secret exists in Azure, in settings, or in source.
- **Nothing about users is stored.** Exchanged tokens are held in memory only, keyed by a hash of the caller's token, and expire with the token. The tool list is the only other cache and holds no user data.
- **The gateway can only call Power Platform.** The hostname is built from a checked GUID plus the fixed suffix `environment.api.powerplatform.com`, so a model-supplied `environmentId` cannot point it at another host.
- **Tool calls are never retried.** A retried call could create or change something twice.
- **Single tenant.** Tokens from tenants other than `Auth:TenantId` are refused.
- **One Entra app, two roles.** The same app is the plugin's sign-in client and the gateway's API. That keeps setup to one registration and one consent.

## Layout

| Path | Purpose |
|------|---------|
| `src/CopilotStudioMcp/` | The server |
| `src/CopilotStudioMcp/Gateway/` | The upstream client, token exchange, tool catalog, and JSON-RPC handler |
| `tests/CopilotStudioMcp.Tests/` | In-process tests for sign-in, environment routing, and error handling |
| `infra/main.bicep`, `infra/deploy.ps1` | Container Apps, Container Registry, Log Analytics, Application Insights, and a deploy script |
| `scripts/New-PluginClientApp.ps1` | Creates the one Entra app that is both the plugin's sign-in client and the gateway's API |

The server runs on .NET 10 as a stateless ASP.NET Core app, so any replica can answer any request. It uses plain JSON-RPC rather than the MCP C# SDK because it relays Copilot Studio's messages as they are, including fields newer than the SDK knows.

## Prerequisites

- An Azure subscription where you can create resources and role assignments
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to run the tests
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) 2.62 or later, signed in with `az login`
- Permission to register applications in your Microsoft Entra tenant, and an admin to grant consent
- To run `deploy.ps1`, you must be able to edit the Entra app (an owner of it, or an Application Administrator), because the script adds the federated credential that lets the managed identity act as the app

## Deploy

1. **Create the plugin's Entra app** and grant consent:

   ```powershell
   .\scripts\New-PluginClientApp.ps1 -GrantAdminConsent
   ```

   It prints your tenant ID and the client ID for the plugin.

2. **Deploy the gateway:**

   ```powershell
   .\infra\deploy.ps1 -ResourceGroup rg-copilot-studio-mcp -TenantId <your tenant ID> -ClientId <client ID from step 1>
   ```

   It provisions the resources, builds the image in Azure Container Registry (no local Docker needed), and starts the app. The last line prints the MCP endpoint. The host, without `https://` or `/mcp`, is the plugin's `COPILOT_STUDIO_GATEWAY_HOST`.

3. **Configure and deploy the plugin.** Follow the plugin's readme.

Useful `deploy.ps1` options: `-AllowedIpRanges` to restrict ingress, `-MinReplicas` (default 1 so the first call has no cold start), `-Location`, and `-AppName`.

## Configuration

| Setting | Default | Meaning |
|---------|---------|---------|
| `Auth:Enabled` | `true` | Turn off only for local development. The server refuses to start with it on and no tenant ID |
| `Auth:TenantId` | none | Tenant whose tokens are accepted |
| `Auth:ClientId` | none | The Entra app that is the gateway's API. The server refuses to start with auth on and no value |
| `Auth:ScopeName` | `access_as_user` | Scope the token must hold |
| `Exchange:Scope` | `https://api.powerplatform.com/.default` | What the on-behalf-of exchange asks Entra for |
| `Exchange:ManagedIdentityClientId` | set by Bicep | The managed identity that proves the server is the Entra app |
| `RateLimit:PermitsPerMinute` | `120` | Per user. `0` turns it off |
| `Gateway:ToolsCacheMinutes` | `10` | How long the tool list is reused |
| `Gateway:UpstreamTimeoutSeconds` | `120` | Longest a single Copilot Studio call may take |
| `Gateway:MaxResponseBytes` | 4 MB | Largest response relayed |

## Run and test locally

```powershell
dotnet test tests/CopilotStudioMcp.Tests
dotnet run --project src/CopilotStudioMcp --environment Development
```

In `Development`, `Auth:Enabled` is `false`, so the server accepts any bearer token on `/mcp`. The token exchange still happens, though: Entra only accepts a real token issued for the gateway's Entra app (scope `access_as_user`), and a local run has no managed identity. To call Copilot Studio locally, sign in to get such a token, create a temporary client secret on the Entra app, and set it as `Exchange__ClientSecret` in your environment. Delete the secret when you are done. Never put it in `appsettings.json` or commit it. The tests need none of this, because they replace the exchange with a fake.

## Troubleshooting

| Symptom | Likely cause and fix |
|---------|----------------------|
| `401` from `/mcp` | The token is missing, expired, for another tenant, or not issued for this gateway's app (for example a Power Platform token). Check `Auth:TenantId` and `Auth:ClientId`, and that the plugin requests `api://<client ID>/access_as_user` |
| `403` from `/mcp` | The token lacks `access_as_user`. Check that the Entra app exposes the scope and has consent for it |
| A tool says the plugin's Entra app has no consent | The exchange for a Power Platform token was refused. Grant admin consent for the app's Power Platform permissions, or sign in again if MFA is required |
| A tool says the gateway has no managed identity configured | The container is missing its identity settings. Run `deploy.ps1` again |
| `list_environments` says the permission was denied | `EnvironmentManagement.Environments.Read` has no admin consent. Run `New-PluginClientApp.ps1 -GrantAdminConsent` or ask an admin |
| A tool says the environment was not found or access was denied | The user has no Copilot Studio access in that environment, or it has no Dataverse database |
| Calls time out | Raise `Gateway:UpstreamTimeoutSeconds`, and check that the caller's own timeout is not shorter |
| Container app does not start | The log stream shows the reason. A missing `Auth:TenantId` stops startup on purpose |
