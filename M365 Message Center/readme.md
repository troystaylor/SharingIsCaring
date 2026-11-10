# M365 Message Center

Read your tenant's Microsoft 365 Message Center posts with the [Microsoft Graph service communications API](https://learn.microsoft.com/graph/api/resources/service-communications-api-overview), from Power Automate, Power Apps, Logic Apps and Copilot Studio. This is a dual-purpose connector: the same deployment gives you standard connector operations and an MCP endpoint for Copilot Studio agents.

Message Center is where Microsoft announces changes that affect your tenant: new features, retirements, required actions and planned maintenance. With this connector you can pull those posts into a flow, filter them by service or deadline, summarize them for your admins, and mark them read or archived as you triage.

A matching [Copilot plugin](../Copilot%20Plugins/M365%20Message%20Center/) lets Microsoft 365 Copilot do the same through a hosted MCP server.

## Choose the right Microsoft source

| You want | Use |
|----------|-----|
| The posts in **your** Message Center, filtered to your tenant's services, with your read and archive status | This connector |
| The public [Microsoft AI at Work Roadmap](https://www.microsoft.com/microsoft-365/roadmap) and [Azure Updates](https://azure.microsoft.com/updates), with no sign-in | The [Release Communications MCP server](https://learn.microsoft.com/microsoft-365/admin/manage/mrc-mcp) at `https://www.microsoft.com/releasecommunications/mcp`, or its Power Platform wrapper, [Microsoft Release Communications MCP](../Microsoft%20Release%20Communications%20MCP/) |
| Microsoft Entra users, groups, apps and devices in natural language | The [Microsoft MCP Server for Enterprise](https://learn.microsoft.com/graph/mcp-server/overview) (preview) |

The roadmap and Message Center complement each other. The roadmap shows what is planned for everyone, and Message Center tells you what is changing in your tenant and what you need to do about it.

## Operations

| Operation | Method | Microsoft Graph |
|-----------|--------|-----------------|
| List Message Center Posts | GET | `/admin/serviceAnnouncement/messages` |
| Get Message Center Post | GET | `/admin/serviceAnnouncement/messages/{id}` |
| List Post Attachments | GET | `/admin/serviceAnnouncement/messages/{id}/attachments` |
| Mark Posts Read / Unread | POST | `markRead`, `markUnread` |
| Archive Posts / Unarchive Posts | POST | `archive`, `unarchive` |
| Favorite Posts / Unfavorite Posts | POST | `favorite`, `unfavorite` |

**List Message Center Posts** returns the most recently changed posts first, each with a short summary. All of the filters are optional and combine:

| Input | What it does |
|-------|--------------|
| Service | Posts that affect one service, for example `Microsoft Teams`, `Exchange Online` or `SharePoint Online`. Use the name as Message Center shows it. |
| Category | `planForChange`, `preventOrFixIssue` or `stayInformed` |
| Severity | `normal`, `high` or `critical` |
| Major Changes Only | Posts marked as a major change |
| Modified Since | Posts published or updated on or after a date, such as `2026-11-01`. A date without a time zone is read as UTC. |
| Action Due Before | Posts with an action-required deadline between now and a date. A date alone covers that whole day. Results are sorted soonest first. |
| Keyword | Text to find in the title or body |
| Max Posts | 1 to 50, default 20 |

One call examines at most 500 posts so you stay inside the service's rate limit. The result reports `totalMatches`, `scannedCount` and `truncated`. When `truncated` is true, add a filter such as Modified Since to narrow the search.

**Get Message Center Post** takes a post from a dropdown (the last 90 days) or an ID such as `MC123456`, and returns the full text as plain text, the links in the post, its tags and services, its dates, the action-required deadline and your read, archived and favorite status. The status operations change only **your** view in the admin center. The posts themselves are never changed.

Narrow the filters to see the next slice of results.

## Trigger

**When a Message Center post is published or updated** starts a flow when Microsoft publishes or revises a post. Message Center doesn't send notifications, so Power Automate checks on a schedule and the flow starts within a few minutes of a change.

| Input | What it does |
|-------|--------------|
| Service, Category, Severity, Major Changes Only, Keyword | The same filters as List Message Center Posts. Only matching posts start the flow. |
| New Posts Only | Skips posts Microsoft revised after publishing. Message Center has no creation date, so this relies on the `Updated message` tag and the `(Updated)` title prefix. |

The trigger returns the changed posts, newest first, in the same shape as List Message Center Posts. A flow runs once for each post.

When you turn the flow on, the newest post sets the starting point and doesn't start the flow, so you won't get a run for posts that already exist. If several posts change between checks, each one starts a run.

## MCP tools

The MCP operation, **Invoke M365 Message Center MCP**, serves four tools:

| Tool | What it does |
|------|--------------|
| `list_messages` | List and search posts with the same filters as List Message Center Posts |
| `get_message` | Get one post by ID |
| `list_message_attachments` | List a post's attachments (name, type, size) |
| `set_message_status` | Mark up to 20 posts read, unread, archived, unarchived, favorited or unfavorited for the signed-in user |

Post IDs must look like `MC123456`, and anything else is refused before a request is made. Dropdowns are a designer feature, so an agent supplies IDs itself. Post text is written by Microsoft and other services, so the tool descriptions tell the agent to summarize it and not to follow instructions that appear inside it.

## Prerequisites

- A Power Platform environment where you can create custom connectors
- [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction) (`pac`), signed in to that environment
- A Microsoft Entra app registration (steps below)
- A Microsoft Entra administrator to grant consent for two delegated Microsoft Graph permissions
- Users who hold an admin role that can read Message Center, such as **Global Reader**, **Service Support Administrator**, **Helpdesk Administrator** or **Global Administrator**. Other users are denied, even after consent.

## Setup

### 1. Register an app in Microsoft Entra ID

```powershell
az ad app create --display-name "M365 Message Center Connector" `
  --sign-in-audience AzureADMyOrg `
  --web-redirect-uris "https://global.consent.azure-apim.net/redirect"

# Delegated Microsoft Graph permissions (resource 00000003-0000-0000-c000-000000000000)
$graph = "00000003-0000-0000-c000-000000000000"
$read  = az ad sp show --id $graph --query "oauth2PermissionScopes[?value=='ServiceMessage.Read.All'].id" -o tsv
$write = az ad sp show --id $graph --query "oauth2PermissionScopes[?value=='ServiceMessageViewpoint.Write'].id" -o tsv
az ad app permission add --id <appId> --api $graph --api-permissions "$read=Scope" "$write=Scope"

az ad app permission admin-consent --id <appId>

# A client secret: Power Platform needs one to complete sign-in
az ad app credential reset --id <appId> --append --display-name "Power Platform connector" --years 1 --query password -o tsv
```

Copy the secret now, because you can't read it again. You'll enter it in step 2.

The two permissions are:

- `ServiceMessage.Read.All`, which listing, reading and attachments need. Only an administrator can consent to it.
- `ServiceMessageViewpoint.Write`, which marking posts read, archived or favorited needs. If you only want to read posts, leave it out of the commands above. The status operations then return `403`.

If `admin-consent` fails right after you create the app, wait a minute and run it again.

### 2. Add your client ID and secret

In `apiProperties.json`, replace `[YOUR_CLIENT_ID]` with your app's Application (client) ID. Leave `[YOUR_REDIRECT_URL]` as it is: the platform replaces it with a per-connector URL when you deploy.

Add the client secret after you deploy: in [Power Automate](https://make.powerautomate.com), open **Custom connectors**, edit **M365 Message Center**, go to **Security**, enter the secret in **Client secret**, and select **Update connector**. Or add `"clientSecret": "<your secret>"` next to `clientId` in a temporary copy of `apiProperties.json` and deploy that copy.

### 3. Deploy the connector

```powershell
pac connector create `
    --api-definition-file apiDefinition.swagger.json `
    --api-properties-file apiProperties.json `
    --script-file script.csx
```

`--script-file` is required because the connector declares script operations. To update an existing connector, add `--connector-id <your connector ID>` and use `pac connector update`.

### 4. Register the redirect URL

The platform generates a redirect URL for your connector that has to be added to the app registration, or sign-in fails. Download the deployed connector and read it:

```powershell
pac connector download --connector-id <your connector ID> --outputDirectory ./verify
# properties.connectionParameters.token.oAuthSettings.redirectUrl
```

Then add it to the app:

```powershell
az ad app update --id <appId> --web-redirect-uris `
    "https://global.consent.azure-apim.net/redirect" "<redirect URL from the download>"
```

### 5. Create a connection

In Power Automate, go to **Connections**, select **New connection**, choose **M365 Message Center** and sign in with an account that holds one of the roles above.

To use the tools in Copilot Studio, add the connector as a tool to your agent and pick the **Invoke M365 Message Center MCP** operation.

## Example flows

**Alert your team when a major Teams change is posted**

1. Trigger: When a Message Center post is published or updated, with Service set to `Microsoft Teams` and Major Changes Only on.
2. Post an adaptive card to your admin channel with the title, severity, action deadline and Admin Center Link.
3. Mark Posts Read for the post's ID once someone acknowledges it.

**Weekly change digest for your admin team**

1. Trigger: recurrence, every Monday.
2. List Message Center Posts with Modified Since set to seven days ago and Category set to `planForChange`.
3. Apply to each post: add a row to your change-tracking list with the title, services, deadline and Admin Center Link.
4. Post the count to a Teams channel.

**Remind owners before an action deadline**

1. Trigger: recurrence, every day.
2. List Message Center Posts with Action Due Before set to 14 days from now.
3. Apply to each post: send the title, the deadline and the Admin Center Link to the service owner.
4. Mark Posts Read for the IDs you handled.

## Troubleshooting

| Symptom | Cause and fix |
|---------|---------------|
| `403 Forbidden` on any operation | The signed-in user doesn't hold an admin role that can read Message Center, or an administrator hasn't consented to `ServiceMessage.Read.All`. Both are required. |
| `403 Forbidden` only on Mark, Archive or Favorite | `ServiceMessageViewpoint.Write` has no admin consent. Add it to the app and grant consent. |
| `AADSTS65001` or "Need admin approval" at sign-in | Admin consent hasn't been granted. Run the `admin-consent` command from step 1, or ask an administrator to. |
| `AADSTS7000215` or `AADSTS7000218` at sign-in, or the connection never completes | The connector has no client secret, or the secret is wrong or expired. Create a new secret on the app and enter it on the connector's **Security** tab. |
| `AADSTS50011` redirect URI mismatch at sign-in | The per-connector redirect URL isn't registered on your app. See step 4. |
| `429 Too Many Requests` | Service communications allows 240 requests per minute and 800 per hour per app and tenant. Each call to List Message Center Posts can use up to five requests, so space out scheduled runs and add filters. |
| `truncated` is true | The search stopped at 500 posts. Add Modified Since, Service or Category. |
| The trigger doesn't start a flow for a post that was already there | Expected. The newest post sets the starting point when you turn the flow on. Only later changes start runs. |
| The trigger fires again for a post you already handled | Microsoft updated the post. Turn on New Posts Only to skip revisions. |
| The trigger never fires | Check the filters first, since Service must match the name Message Center shows. Then confirm the connection's user holds a Message Center admin role. |
| The post dropdown is empty | The dropdown lists posts from the last 90 days. You can still type an ID such as `MC123456`. |
| `400` that a post ID looks like `MC123456` | The ID has extra characters or isn't a Message Center ID. |
| `404 Not Found` | The post doesn't exist in your tenant. IDs from another tenant or an old export may not resolve. |
| `InvalidScriptDefinitionUrlWithNonNullOperations` on deploy | `--script-file` was left off. Add it. |
| The MCP endpoint, List Message Center Posts, Get Message Center Post or the dropdown doesn't respond | The connector was deployed without its script. Redeploy with `--script-file`. |

## Government and national clouds

The service communications API is available in US Government and China clouds. To use one, change `host` in `apiDefinition.swagger.json` and `GraphBaseUrl` in `script.csx` to your cloud's Graph endpoint from [Microsoft Graph national clouds](https://learn.microsoft.com/graph/deployments), and use that cloud's Entra authority in the OAuth URLs.

## Telemetry

Application Insights logging is off by default. To turn it on, set `APP_INSIGHTS_ENABLED` to `true` and replace the instrumentation key placeholder in `script.csx`. The script sends request and tool-call events, with the method, tool name and status code, and no post content.

## Resources

- [Working with the service communications API](https://learn.microsoft.com/graph/api/resources/service-communications-api-overview)
- [List serviceAnnouncement messages](https://learn.microsoft.com/graph/api/serviceannouncement-list-messages)
- [serviceUpdateMessage resource type](https://learn.microsoft.com/graph/api/resources/serviceupdatemessage)
- [Message Center in the Microsoft 365 admin center](https://learn.microsoft.com/microsoft-365/admin/manage/message-center)
