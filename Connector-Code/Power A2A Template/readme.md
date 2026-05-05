# Power A2A Template

Reusable Power Platform custom MCP connector template for the [Agent-to-Agent (A2A) protocol](https://a2a-protocol.org/). Acts as an A2A v1.0 client — connects Power Automate and Copilot Studio to any A2A-compliant agent.

## What This Template Does

- **Power Automate**: 10 REST operations for sending messages, managing tasks, and configuring push notifications
- **Copilot Studio**: 6 MCP tools that wrap A2A operations, giving your agent selective tool-use control over external agents (vs. native A2A which delegates fully)
- **Dual binding**: Supports both JSON-RPC (Work IQ, others) and HTTP+JSON (REST) protocol bindings
- **Universal**: Works with Work IQ, Salesforce Agentforce, Google Vertex agents, LangGraph, CrewAI, or any A2A v1.0 agent

## When to Use This vs. Native A2A in Copilot Studio

| Scenario | Use This | Use Native A2A |
|---|---|---|
| Power Automate flows (scheduled, event-driven) | Yes — only option | Not available |
| Copilot Studio — inspect response before showing user | Yes — tool returns data to your agent | No — full delegation |
| Copilot Studio — fan out to multiple agents | Yes — call each as a tool | No — one agent at a time |
| Copilot Studio — simple delegation to one agent | Either works | Simpler setup |

## Prerequisites

- An A2A agent endpoint URL
- Authentication credentials for the agent (OAuth, API Key, or none)
- For Work IQ: Microsoft 365 Copilot license + admin consent for `WorkIQAgent.Ask`

## Configuration

### 1. Edit script.csx Constants

Open `script.csx` and update the configuration section at the top:

```csharp
// Your A2A agent endpoint
private const string A2A_ENDPOINT = "https://your-agent.example.com/a2a/";

// Protocol binding: "jsonrpc" (Work IQ, default) or "httpjson" (REST)
private const string A2A_PROTOCOL_BINDING = "jsonrpc";

// A2A protocol version
private const string A2A_VERSION = "1.0";

// Optional: target specific agent on multi-agent gateways
private const string A2A_DEFAULT_AGENT_ID = "";

// Optional: multi-tenant endpoint prefix
private const string A2A_TENANT = "";

// Optional: Application Insights connection string
private const string APP_INSIGHTS_CONNECTION_STRING = "";
```

### 2. Configure Authentication in apiProperties.json

The template ships with OAuth 2.0 for Work IQ. Replace `connectionParameters` based on your agent's auth:

**OAuth 2.0 (default — configured for Work IQ):**
```json
"connectionParameters": {
    "token": {
        "type": "oauthSetting",
        "oAuthSettings": {
            "identityProvider": "aad",
            "clientId": "[[REPLACE_WITH_APP_ID]]",
            "scopes": "api://workiq.svc.cloud.microsoft/WorkIQAgent.Ask offline_access",
            "redirectMode": "Global",
            "redirectUrl": "https://global.consent.azure-apim.net/redirect",
            "customParameters": {
                "loginUri": { "value": "https://login.microsoftonline.com" },
                "tenantId": { "value": "common" },
                "resourceUri": { "value": "api://workiq.svc.cloud.microsoft" }
            }
        }
    }
}
```

**API Key:**
```json
"connectionParameters": {
    "apiKey": {
        "type": "securestring",
        "uiDefinition": {
            "displayName": "API Key",
            "description": "API key for the A2A agent",
            "tooltip": "Enter the API key provided by the agent",
            "constraints": { "required": "true" }
        }
    }
}
```

**No Auth:**
```json
"connectionParameters": {}
```

### 3. Deploy

```bash
paconn create -s "Power A2A Template" \
  --api-def apiDefinition.swagger.json \
  --api-prop apiProperties.json \
  --script script.csx
```

## REST Operations (Power Automate)

| Operation | Description |
|---|---|
| **Send Message** | Send a message and wait for the agent's completed response |
| **Send Message (Async)** | Send a message and return immediately with a task ID |
| **Get Task** | Poll task status, artifacts, and history by task ID |
| **List Tasks** | List tasks filtered by context ID, state, or pagination |
| **Cancel Task** | Cancel an in-progress task |
| **Get Agent Card** | Discover agent identity, skills, and capabilities |
| **Create Push Notification Config** | Configure webhook notifications for task updates |
| **Get Push Notification Config** | Retrieve a push notification config |
| **List Push Notification Configs** | List all push configs for a task |
| **Delete Push Notification Config** | Delete a push notification config |

## MCP Tools (Copilot Studio)

| Tool | Description |
|---|---|
| `send_message` | Sync delegation — send message, wait for response |
| `send_message_async` | Fire-and-forget — returns task ID for polling |
| `get_task` | Poll task status and retrieve artifacts |
| `list_tasks` | Filter tasks by context or state |
| `cancel_task` | Cancel an in-progress task |
| `get_agent_card` | Runtime discovery of agent capabilities |

## Multi-Turn Conversations

Pass the `contextId` from a previous response to continue a conversation:

**Power Automate:** Use the `Context ID` output from Send Message as input to the next Send Message action.

**Copilot Studio (MCP):** The `send_message` tool accepts `context_id` — pass the value from the previous response.

## Protocol Bindings

### JSON-RPC (default)

Used by Work IQ and others. All requests POST to a single endpoint with the method name inside the JSON-RPC body:

```json
POST https://workiq.svc.cloud.microsoft/a2a/
{
  "jsonrpc": "2.0",
  "id": "request-123",
  "method": "SendMessage",
  "params": {
    "message": {
      "role": "ROLE_USER",
      "messageId": "msg-456",
      "parts": [{ "text": "What meetings do I have today?" }]
    }
  }
}
```

### HTTP+JSON

Standard REST paths. Set `A2A_PROTOCOL_BINDING = "httpjson"` in script.csx:

```
POST /message:send       → Send Message
GET  /tasks/{id}          → Get Task
GET  /tasks               → List Tasks
POST /tasks/{id}:cancel   → Cancel Task
```

## Work IQ Setup

If targeting Work IQ specifically, follow these one-time admin steps:

1. **Create the Work IQ service principal** — POST to `https://graph.microsoft.com/v1.0/servicePrincipals` with body `{ "appId": "fdcc1f02-fc51-4226-8753-f668596af7f7" }`

2. **Create an app registration** in Microsoft Entra admin center:
   - Supported account types: single tenant
   - Add redirect URI: `https://global.consent.azure-apim.net/redirect`
   - API permissions: add `WorkIQAgent.Ask` (delegated) under "Work IQ"
   - Grant admin consent

3. **Update apiProperties.json** with your App ID in the `clientId` field

4. **Required**: Users need a Microsoft 365 Copilot license

See [Work IQ API quickstart](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-api-quickstart) for detailed instructions.

## Location Metadata

Work IQ requires location metadata for time-sensitive queries ("today", "this week"). Include `timeZone` and `timeZoneOffset` in your Send Message requests:

- **Time Zone**: IANA format (e.g., `America/Los_Angeles`)
- **Time Zone Offset**: UTC offset in minutes (e.g., `-480` for PST)

## Links

- [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- [A2A Protocol Definitions](https://a2a-protocol.org/latest/definitions/)
- [Work IQ API Overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-api-overview)
- [Work IQ Samples](https://github.com/microsoft/work-iq-samples)
- [Work IQ CLI](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-cli)
- [Work IQ API Terms of Use](https://learn.microsoft.com/en-us/legal/work-iq-apis/terms-of-use)
