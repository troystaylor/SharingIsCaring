# Copilot Studio

A Microsoft 365 Copilot plugin that connects to the [Copilot Studio MCP server](https://github.com/mcp/com.microsoft/copilot-studio) so you can build, test and manage Copilot Studio agents and workflows from Copilot, in any Power Platform environment you can access. Requests run as the signed-in user, under that user's Copilot Studio and Power Platform permissions.

> **Preview.** The Copilot Studio MCP server is new and its tool actions can change. The skills tell Copilot to read each action's contract at run time instead of relying on fixed action names.

## What's in the plugin

| Component | Path |
| --- | --- |
| Connector `copilot-studio` (remote MCP gateway, OAuth with PKCE through the Teams Developer Portal) | `appPackage/manifest.json` |
| Shared skills: `copilot-studio-access`, `write-safety` | `appPackage/skills/*/SKILL.md` |
| Scenario skills: `agent-authoring`, `workflow-authoring-and-diagnostics`, `agent-evaluation`, `consumption-review`, `connector-discovery` | `appPackage/skills/*/SKILL.md` |

The connector offers `list_environments` plus the five Copilot Studio tools (`get_copilot_studio_skills`, `manage_agents`, `manage_workflows`, `discover_resources`, `get_consumption`). Every tool except `list_environments` takes an `environmentId`.

| Skill | Use it to | Changes things |
| --- | --- | --- |
| `agent-authoring` | Create or edit an agent as a draft, preview it, then publish | Yes, after confirmation |
| `workflow-authoring-and-diagnostics` | Build workflows, run real tests, diagnose failed runs | Yes, after confirmation |
| `agent-evaluation` | Create and run evaluation suites and read the results | Yes, after confirmation |
| `connector-discovery` | Find connector operations, schemas, connections and parameter values | No |
| `consumption-review` | See credit consumption by agent or workflow and remaining capacity | No |

## How environments work

Microsoft hosts the Copilot Studio MCP server once per Power Platform environment, but a plugin can point at only one URL. This plugin points at a small gateway that you host in Azure. The gateway lists the environments the signed-in user can access, and sends each request to the environment Copilot chose. Copilot asks which environment to use when it isn't clear, and names the environment before it changes anything.

The gateway keeps no secrets and no user data. It checks the user's sign-in token, trades it for a Power Platform token for that same user, and calls Copilot Studio with it, so Copilot Studio permissions still decide what each person can do. Its source, deployment script and design notes are in the `Copilot Studio MCP Server` folder under `Container Apps`.

## Prerequisites

- A Microsoft 365 work account with a Copilot license and permission to upload custom apps (or an admin who can upload for you).
- Power Platform environments with Copilot Studio, and a Copilot Studio role in each environment you want to use.
- An Azure subscription to host the gateway, and permission to register an app in Microsoft Entra ID. An admin must grant consent for the permissions below.
- [Work IQ Dev Tools (`wiqd`)](https://aka.ms/wiqd/docs), signed in with `wiqd auth login --interactive`.

## Setup

1. **Create the Entra app** the plugin signs in with. From the gateway folder, run:

   ```powershell
   .\scripts\New-PluginClientApp.ps1 -GrantAdminConsent
   ```

   It creates one app that is both the plugin's sign-in client and the gateway's API. It is a public client (no client secret, PKCE) with the redirect URI `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` on the **Mobile and desktop applications** platform, and it exposes the scope `access_as_user`. So the gateway can act for the user, it also requests two delegated permissions on the **Power Platform API** (`https://api.powerplatform.com`): `CopilotFlows.ChatAssistant.Execute` and `EnvironmentManagement.Environments.Read`. It prints your tenant ID and client ID.

2. **Deploy the gateway** with the script in the gateway folder:

   ```powershell
   .\infra\deploy.ps1 -ResourceGroup rg-copilot-studio-mcp -TenantId <your tenant ID> -ClientId <client ID from step 1>
   ```

   The last line prints the MCP endpoint, for example `https://cstudiomcp.<region>.azurecontainerapps.io/mcp`. The host alone, without `https://` or `/mcp`, is your gateway host.

3. **Set your values** in `env/.env.dev` (`env/.env.local` for local use):

   ```
   COPILOT_STUDIO_TENANT_ID=<your tenant ID>
   COPILOT_STUDIO_CLIENT_ID=<client ID from step 1>
   COPILOT_STUDIO_GATEWAY_HOST=<gateway host from step 2>
   ```

   Leave `TEAMS_APP_ID` and `COPILOT_STUDIO_OAUTH_REFERENCE_ID` empty. Provisioning fills them in.

4. **Provision, package and share.** Use `--env dev` on each command; without it they target the `local` environment, which isn't provisioned:

   ```powershell
   wiqd plugin provision --env dev
   wiqd plugin package --env dev
   wiqd plugin share --env dev --email <user@yourtenant.com>
   ```

   `provision` registers the OAuth client and writes the app and reference IDs to `env/.env.dev`; run it before `package`. Each time you provision again after the first, raise `version` in `appPackage/manifest.json` first, or the update is rejected.

5. **Deploy to your organization.** In the [Microsoft 365 admin center](https://admin.microsoft.com), go to **Agents > Tools**, upload the package from `appPackage/build`, and assign it to the users or groups who should have it. Admins can review, enable, disable or block it there.

6. **Sign in on first use.** When a user first asks for something that needs the connector, Copilot prompts for sign-in. After consent, try "List my Copilot Studio environments" and then "List the agents in <environment>."

## Using it

- Describe what you want: "Create an agent that answers HR policy questions from our SharePoint site in my Dev environment", "Why did my expense workflow fail yesterday?", "Which agents used the most credits this month?"
- Copilot picks the environment with you, reads the Copilot Studio guidance first, previews every change, and asks for a yes before it creates, edits, publishes, shares or runs anything with side effects.
- Changes are made to drafts. Publishing and sharing are separate confirmations.

## Troubleshooting

| Symptom | Likely cause and fix |
| --- | --- |
| "Need admin approval" at sign-in | Admin consent has not been granted. Run `New-PluginClientApp.ps1 -GrantAdminConsent` as an admin, or run `az ad app permission admin-consent --id <client ID>`. |
| "Something went wrong" after you sign in, with a Teams redirect URL in the address bar | Entra rejected the token request with `AADSTS9002327`. The redirect URI is registered on the **Single-page application** platform. Move it to **Mobile and desktop applications** and remove it from the single-page platform. |
| `CopilotFlows.ChatAssistant.Execute` is not available when adding permissions | The Power Platform API service principal may not exist in your tenant yet. `New-PluginClientApp.ps1` creates it. To do it by hand, run `New-MgServicePrincipal -AppId 8578e004-a5c6-46e7-913e-12f58912df43` (Microsoft Graph PowerShell), then add the permission again. |
| "Could not verify connection" in Copilot | The connector could not reach the gateway. Check `COPILOT_STUDIO_GATEWAY_HOST`, open `https://<gateway host>/healthz`, and confirm the OAuth reference ID is set in `env/.env.dev`. |
| Tool calls return 404 after a successful provision, or you need to change the OAuth scope or gateway host | Provisioning creates the OAuth registration once and never rewrites it. Delete the registration in the Teams Developer Portal (Tools > OAuth client registration), clear `COPILOT_STUDIO_OAUTH_REFERENCE_ID` in `env/.env.dev`, raise `version` in `appPackage/manifest.json`, and provision again. |
| Copilot says listing environments was denied | `EnvironmentManagement.Environments.Read` has no admin consent. Grant it as above. |
| 403 or "denied access" for one environment | Your account has no Copilot Studio role in that environment, or a data policy blocks the action. Ask that environment's admin. |
| An environment is missing from the list | Only enabled environments you can access are listed. Ask an admin to check your access. |
| The plugin does not appear in Copilot | Confirm it is assigned to you in **Agents > Tools** and that sharing finished. |

## Limits

- The gateway supports one Microsoft Entra tenant and the public Power Platform cloud.
- In tenants that require FedRAMP High, the Copilot Studio MCP harness agent is turned off at run time.
- The manifest has no `mcpToolDescription`, so Copilot uses the gateway's live tool list. If you target Copilot Cowork and it reports "Could not verify connection", capture the gateway's `tools/list` output to a file under `appPackage/` and add it with `wiqd plugin add connector --tool-description`.
- Skills cannot exceed what your Copilot Studio permissions allow. OAuth consent and your environment roles are the hard limits; the skills add a second layer of confirmation.
