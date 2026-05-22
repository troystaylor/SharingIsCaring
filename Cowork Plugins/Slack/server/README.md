# Slack Cowork MCP — server

In-tenant Model Context Protocol server that bridges Microsoft 365 Copilot
Cowork to Slack. All Slack Web API egress originates from your Azure
subscription; no vendor-hosted MCP is in the path.

The same backend exposes two routes off ASP.NET Core, each returning a
different `tools/list`:

| Route | tools/list | Used by |
|---|---|---|
| `POST /mcp/full` | All 14 tools (read + write) | Cowork plugin (`agentConnectors[].toolSource.remoteMcpServer.mcpServerUrl`) |
| `POST /mcp/federated` | Read-only subset only | M365 admin center custom federated connector |

## Tools

Read-only (`readOnlyHint: true`):

- `search_messages` — Slack search syntax
- `list_channels` — `conversations.list`
- `get_channel_history` — `conversations.history` (auto-paginates)
- `get_user_info` — resolves handles → IDs via `users.list`
- `list_users` — `users.list`
- `scan_slack` — ranks Slack Web API methods against a natural-language intent

Write (`destructiveHint: true`):

- `send_message` — `chat.postMessage`
- `schedule_message` — `chat.scheduleMessage`
- `pin_message` — `pins.add`
- `add_bookmark` — `bookmarks.add`
- `complete_or_delete_reminder` — `reminders.complete` / `reminders.delete`
- `upload_file` — v2 external flow (`getUploadURLExternal` → PUT → `completeUploadExternal`)
- `launch_slack` — generic invoker; validates `endpoint` against the capability index
- `sequence_slack` — array of launch_slack-shaped requests, stop-on-error or continue

The `destructiveHint` annotation is what drives Cowork's confirm dialog. Tools
that surface in the M365 federated route are the `readOnlyHint` set only.

## Auth flow

1. Cowork forwards the user's Slack OAuth token (`xoxp-…`) from the M365
   Enterprise Token Store binding `slack-oauth` as
   `Authorization: Bearer <xoxp-…>` on every MCP call.
2. The MCP server reads that header per request via `IBearerTokenAccessor`.
3. `SlackClient` attaches the same bearer to every outbound
   `https://slack.com/api/*` call.
4. Missing token → JSON-RPC error `-32001 "unauthorized: missing user token"`.

The Slack OAuth client secret is provisioned in Key Vault for completeness
and future flows, but the runtime path uses only the per-request bearer.

## Local dev

Set a Slack user OAuth token (one with the scopes you intend to test):

```pwsh
$env:SLACK_DEV_USER_TOKEN = "xoxp-..."
dotnet run --project ./SlackCoworkMcp.csproj --urls http://localhost:8080
```

When `SLACK_DEV_USER_TOKEN` is set, the server falls back to it if no
`Authorization` header is present. In production deployments, leave that env
var unset so the only auth path is the inbound bearer from Cowork.

### tools/list

```pwsh
curl http://localhost:8080/mcp/full `
  -H "Content-Type: application/json" `
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

### tools/call — send_message

```pwsh
curl http://localhost:8080/mcp/full `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer xoxp-your-user-token" `
  -d '{
    "jsonrpc":"2.0",
    "id":2,
    "method":"tools/call",
    "params":{
      "name":"send_message",
      "arguments":{"channel":"C0123456789","text":"hello from MCP"}
    }
  }'
```

### tools/call — scan_slack

```pwsh
curl http://localhost:8080/mcp/full `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer xoxp-your-user-token" `
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"scan_slack","arguments":{"query":"set my status to away"}}}'
```

## Deploy to Azure

From `../infra`:

```pwsh
azd auth login
azd env new slack-cowork-dev --subscription <SUBSCRIPTION_ID> --location eastus2
azd env set DEPLOYER_PRINCIPAL_ID (az ad signed-in-user show --query id -o tsv)
azd env set SLACK_CLIENT_SECRET <slack-client-secret>
azd provision --preview
azd up
```

`azd up` builds the Dockerfile in this directory, pushes it to the ACR
provisioned by Bicep, and deploys it as the Container App configured in
`../infra/main.bicep`.

After deployment, copy the Container App / APIM URL into the parent
`Cowork Plugins/Slack/manifest.json` `mcpServerUrl` (append `/mcp/full`),
repackage, and side-load into Microsoft 365.

## Federated route — M365 admin center

The read-only route at `/mcp/federated` is intended to be registered in the
M365 admin center as a **custom federated connector** so it surfaces in
M365 Search results. Audience and audience-token validation happen at APIM
(see `infra/modules/apim.bicep`), so the route is gated by the Entra app
registration's identifier URI.

## Confirmation behaviour

MCP annotations on each tool:

| Tool | `readOnlyHint` | `destructiveHint` | Result in Cowork |
|---|---|---|---|
| `search_messages`, `list_channels`, `get_channel_history`, `get_user_info`, `list_users`, `scan_slack` | true | false | runs silently |
| `send_message`, `schedule_message`, `pin_message`, `add_bookmark`, `complete_or_delete_reminder`, `upload_file`, `launch_slack`, `sequence_slack` | true | true | Cowork prompts the user to confirm before invoking |

Annotation titles (e.g. `"Send Slack message"`) are displayed in the
confirmation dialog.

## Layout

```
server/
├── SlackCoworkMcp.csproj
├── Program.cs
├── Endpoints/McpEndpoints.cs         # JSON-RPC over POST; per-route tool filtering
├── Tools/
│   ├── ToolDescriptor.cs, ToolRegistry.cs, ToolHelpers.cs
│   └── *Tool.cs (14 tools)
├── Slack/
│   ├── SlackClient.cs                # bearer-forwarding HttpClient + 429 retry
│   ├── SlackCapabilityIndex.cs       # BM25 scorer
│   ├── CapabilityIndex.json          # embedded resource (curated 70+)
│   ├── SlackApiException.cs
│   └── build-capability-index.ps1    # TODO scraper stub
├── Auth/BearerTokenForwarding.cs
├── appsettings.json
├── Dockerfile                        # multi-stage net10, non-root
└── README.md
```
