# Atlassian MCP V2

Script-free Power Platform custom connector for the [Atlassian MCP server (v2)](https://support.atlassian.com/atlassian-ai-gateway/docs/get-started-with-the-atlassian-remote-mcp-server/) at `https://mcp.atlassian.com/v2/mcp`. It lets Copilot Studio agents work with Jira, Confluence, Jira Service Management, Bitbucket Cloud, Loom, Goals, Projects, Teams and Teamwork Graph. Every call runs as the signed-in user with that user's Atlassian permissions.

This connector is MCP only. It has one operation, `InvokeMCP`, and no standard Power Automate or Power Apps actions. The matching plugin for Microsoft 365 Copilot is in [Copilot Plugins/Atlassian MCP V2](../Copilot%20Plugins/Atlassian%20MCP%20V2/README.md).

## How the server exposes tools

The server doesn't send its whole tool catalog up front. It lists a few primary tools (for example `getJiraIssue`, `searchJiraIssuesUsingJql`, `createJiraIssue`, `getConfluenceContent`, `searchConfluence`, `getTeamworkGraphContext`) and reaches everything else through `discover` plus `executeRead`, `executeWrite` and `executeDestructive`. Your agent calls `discover` with a plain-language goal, then runs the tool it returns. New Atlassian tools appear without any change to this connector.

Some tools use Rovo credits from your organization's shared pool, including `search`, `searchAtlassian` and the Teamwork Graph tools. See [Rovo usage limits](https://support.atlassian.com/rovo/docs/rovo-usage-limits/).

## Files

| File | Purpose |
|---|---|
| `apiDefinition.swagger.json` | The `InvokeMCP` operation and the OAuth endpoints. |
| `apiProperties.json` | OAuth settings for the `oauth2pkcewithdcr` identity provider (public client with PKCE). |
| `apiProperties.oauth2pkce.json` | Alternative OAuth settings for the `oauth2pkce` identity provider (client secret with PKCE). |
| `Register-AtlassianClient.ps1` | Creates an Atlassian OAuth client for your connector's callback URL. |

There is no `script.csx`. Power Platform sends MCP traffic straight to Atlassian.

## Prerequisites

- An Atlassian Cloud site with at least one supported product.
- A Power Platform environment where you can create custom connectors and Copilot Studio agents.
- For the PAC CLI options: [PAC CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction) signed in to that environment, and PowerShell.

Atlassian has no developer-console app for its MCP server. Clients are created through dynamic client registration (DCR), either by Copilot Studio for you (Option A) or with `Register-AtlassianClient.ps1` (Options B and C). Your Atlassian organization admin controls which tools, scopes and client domains are allowed under **Atlassian Administration > Rovo MCP server**.

## Choose how to sign in

| Option | Identity provider | Use it when |
|---|---|---|
| **A. Copilot Studio Add MCP wizard** | Created by the wizard | You want the fastest path. No files and no client registration. |
| **B. PAC CLI, public client** | `oauth2pkcewithdcr` | You want a connector you can version, share and deploy across environments. |
| **C. PAC CLI, client secret** | `oauth2pkce` | Option B signs in but token refresh fails, or you want a confidential client. |

`oauth2pkcewithdcr` and `oauth2pkce` are hidden Power Platform identity providers that you can only set in `apiProperties.json`. They aren't documented, so test sign-in and token refresh in a non-production environment first.

## Option A: Copilot Studio Add MCP wizard

1. In Copilot Studio, open your agent and go to **Tools > Add a tool > New tool > Model Context Protocol**.
2. Enter a name such as `Atlassian`, a description, and the server URL `https://mcp.atlassian.com/v2/mcp`.
3. For authentication, choose **OAuth 2.0** and select **Dynamic discovery**. If discovery fails, try **Dynamic**.
4. Select **Create**, then **Create new connection** and sign in to Atlassian. Approve the consent screen for the site you want to use.
5. Select **Add and configure**.

The wizard creates the connector and registers the client with Atlassian for you.

## Option B: PAC CLI with `oauth2pkcewithdcr`

Run the commands from this folder. Replace `<ENV>` with your environment ID or URL.

1. Deploy the connector. Leave `[YOUR_CLIENT_ID]` and `[YOUR_REDIRECT_URL]` as they are. Power Platform replaces the redirect URL with the callback URL it generates for your connector.

   ```powershell
   pac connector create `
       --environment <ENV> `
       --api-definition-file .\apiDefinition.swagger.json `
       --api-properties-file .\apiProperties.json
   ```

   Don't pass `--script-file`. Note the connector ID that PAC returns.

2. Download the deployed connector and read its callback URL.

   ```powershell
   pac connector download --environment <ENV> --connector-id <CONNECTOR_ID> --outputDirectory .\deployed
   (Get-Content .\deployed\apiProperties.json -Raw | ConvertFrom-Json).properties.connectionParameters.token.oAuthSettings.redirectUrl
   ```

   The URL looks like `https://global.consent.azure-apim.net/redirect/<your-connector>`. It's specific to this connector and environment.

3. Register an Atlassian client for that URL.

   ```powershell
   .\Register-AtlassianClient.ps1 -RedirectUrl "<CALLBACK_URL>"
   ```

   Copy the `client_id` it prints.

4. Put the client ID in a working copy of `apiProperties.json` and update the connector.

   ```powershell
   $props = Join-Path $env:TEMP 'apiProperties.json'
   (Get-Content .\apiProperties.json -Raw).Replace('[YOUR_CLIENT_ID]', '<CLIENT_ID>') | Set-Content $props
   pac connector update `
       --environment <ENV> `
       --connector-id <CONNECTOR_ID> `
       --api-definition-file .\apiDefinition.swagger.json `
       --api-properties-file $props
   ```

5. Create the connection. In [Power Automate](https://make.powerautomate.com), open **Custom connectors**, edit **Atlassian MCP V2**, go to the **Test** tab, select **New connection** and sign in to Atlassian. Leave any client secret field blank.

6. Add the connector to your agent. In Copilot Studio, go to **Tools > Add a tool > Connector**, choose **Atlassian MCP V2**, and select the connection.

If you change the connector later, delete and recreate any connection made before the change.

## Option C: PAC CLI with `oauth2pkce`

Follow Option B with these differences:

- Use `apiProperties.oauth2pkce.json` in place of `apiProperties.json`.
- In step 3, run `Register-AtlassianClient.ps1` with `-Confidential`. The script copies the client secret to your clipboard and doesn't print it.
- After step 4, open the connector in Power Automate, go to the **Security** tab, enter the client ID and the client secret, and select **Update connector**. Clear your clipboard afterward.

## Scopes

Scopes are what the token can do, so they set the connector's limits regardless of what an agent tries. The default set in `apiProperties.json` and `apiDefinition.swagger.json` is:

| Scope | Grants |
|---|---|
| `read:me`, `read:account`, `offline_access` | Your profile, and a refresh token so you stay signed in |
| `read:jira:agent-interface`, `search:jira:agent-interface` | Read and search Jira |
| `write:jira:agent-interface` | Create and update work items, comments, worklogs, links |
| `read:confluence:agent-interface`, `search:confluence:agent-interface` | Read and search Confluence |
| `write:confluence:agent-interface` | Create and update pages and comments |
| `read:bitbucket:agent-interface` | Read Bitbucket Cloud repositories, pull requests and pipelines |

To make the connector read-only, remove the two `write:` scopes. To add more, append them to the `scopes` array in `apiProperties.json` and to both `securityDefinitions.oauth2_auth.scopes` and `security[0].oauth2_auth` in `apiDefinition.swagger.json`, then update the connector and recreate your connections.

| Add this scope | For |
|---|---|
| `read:jsm:agent-interface`, `write:jsm:agent-interface` | Jira Service Management requests and operations alerts |
| `read:loom:agent-interface`, `write:loom:agent-interface` | Loom videos, transcripts and meeting action items |
| `read:goals:agent-interface`, `read:projects:agent-interface`, `read:teams:agent-interface` | Goals, Projects and Teams |
| `search:code:agent-interface` | Code search across connected code providers |
| `read:all:twg`, `search:rovo:agent-interface` | Teamwork Graph and Rovo search (use Rovo credits) |

Leave out `delete:jira:agent-interface` and `manage:jira:agent-interface` unless you need them. Atlassian disables those tool groups by default and an admin must enable them.

## Use it in an agent

Add instructions that tell the agent how the server works. For example:

> Start every Atlassian task by calling `getAccessibleAtlassianResources` to get the `cloudId`. If the tool you need isn't listed, call `discover` with your goal, then run the tool it returns with `executeRead` for reads or `executeWrite` for changes. Confirm with me before any write.

## Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| Sign-in page reports the redirect URI isn't registered | The client was registered for a different callback URL. Run `Register-AtlassianClient.ps1` again with the exact `redirectUrl` from `pac connector download`, and put the new client ID in the connector. |
| Sign-in is blocked for your organization | An Atlassian admin restricts client domains or tools. Ask them to allow `global.consent.azure-apim.net` under **Atlassian Administration > Rovo MCP server**. |
| Sign-in works but tool calls return 401 | Delete the connection and create a new one. Do this after every connector update. |
| A tool is missing or returns access denied | The scope wasn't requested or consented, an admin disabled the tool's permission group, or you lack permission in the product. Check the scopes above. |
| Tools stop working after about an hour | Token refresh failed. Confirm `offline_access` is in the scopes, then try Option C. |
| Jira Service Management tools are missing | Add the `read:jsm:agent-interface` scope and reconsent. |
| Bitbucket tools return nothing | They work only for Bitbucket Cloud workspaces linked to your Atlassian organization. |

## Notes

- Atlassian plans to move v1 callers to v2 tools on March 1, 2027. This connector targets v2 only.
- Gateways that need every tool in one `tools/list` use `https://mcp.atlassian.com/v2/mcp?tools=all`. A connector's base path can't carry a query string, so that URL isn't available here.
- Every call is recorded in your organization's Atlassian audit log.

## Author

Troy Taylor, [troy@troystaylor.com](mailto:troy@troystaylor.com), [github.com/troystaylor](https://github.com/troystaylor)
