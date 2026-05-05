# Agent2Agent

Power Platform custom MCP connector for the [Agent-to-Agent (A2A) protocol](https://a2a-protocol.org/). Connects Power Automate and Copilot Studio to any A2A v1.0 agent. Pre-configured for [Work IQ](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-api-overview) — Microsoft's AI-native interface to Microsoft 365 work intelligence.

## Publisher: Troy Taylor

## Overview

This connector acts as an A2A v1.0 client, enabling:

- **Power Automate flows** that send messages to A2A agents on a schedule, in response to events, or in batch — something native Copilot Studio A2A cannot do
- **Copilot Studio agents** that call A2A agents as tools with selective control — inspect responses, fan out to multiple agents, apply business logic before surfacing results

Works with Work IQ, Salesforce Agentforce, Google Vertex agents, LangGraph, CrewAI, or any A2A v1.0-compliant agent.

## Prerequisites

- Microsoft 365 Copilot license (required for Work IQ)
- Azure AD app registration with `WorkIQAgent.Ask` delegated permission
- Admin consent for the Work IQ service principal

## Work IQ Setup (One-Time Per Tenant)

### Step 1: Create the Work IQ Service Principal

Using [Graph Explorer](https://developer.microsoft.com/graph/graph-explorer) signed in as an admin:

1. Set method to **POST** and URL to `https://graph.microsoft.com/v1.0/servicePrincipals`
2. Consent to `Application.ReadWrite.All`
3. Request body:
```json
{ "appId": "fdcc1f02-fc51-4226-8753-f668596af7f7" }
```
4. Run query — 201 Created confirms success (conflict = already exists, OK to proceed)

### Step 2: Create the App Registration

1. Go to [Microsoft Entra admin center](https://entra.microsoft.com/) → App registrations → New registration
2. Set **Supported account types** to "Accounts in this organizational directory only"
3. Register, then copy the **Application (client) ID** — this is your `APP_ID`
4. **Authentication** → Add a platform → Web → Add redirect URI: `https://global.consent.azure-apim.net/redirect`
5. **API permissions** → Add a permission → APIs my organization uses → search "Work IQ" → Delegated → `WorkIQAgent.Ask` → Add
6. **Grant admin consent** for your tenant
7. Copy your **Directory (tenant) ID**

### Step 3: Update the Connector

Edit `apiProperties.json` and replace `[[REPLACE_WITH_APP_ID]]` with your App ID.

### Step 4: Deploy

```bash
paconn create -s "Agent2Agent" \
  --api-def apiDefinition.swagger.json \
  --api-prop apiProperties.json \
  --script script.csx
```

## Operations

### Send Message

Send a natural language message to the A2A agent and wait for a completed response.

| Parameter | Required | Description |
|---|---|---|
| Message | Yes | The message to send |
| Context ID | No | Continue a multi-turn conversation |
| Agent ID | No | Target a specific agent on multi-agent gateways |
| Time Zone | No | IANA time zone (e.g., `America/Los_Angeles`) — required by Work IQ for time queries |
| Time Zone Offset | No | UTC offset in minutes (e.g., `-480` for PST) |

**Returns:** `taskId`, `contextId`, `state`, `responseText`, `artifacts[]`

### Send Message (Async)

Same as Send Message but returns immediately with a task ID. Use **Get Task** to poll.

### Get Task

Poll a task by ID to check status and retrieve artifacts.

| Parameter | Required | Description |
|---|---|---|
| Task ID | Yes | The task ID from Send Message (Async) |
| History Length | No | Max messages from history to include |

### List Tasks

Filter tasks by context or state with pagination.

| Parameter | Required | Description |
|---|---|---|
| Context ID | No | Filter by conversation context |
| State | No | Filter by state (submitted, working, completed, failed, canceled, input_required, auth_required, rejected) |
| Page Size | No | Max results (1-100, default 50) |
| Page Token | No | Pagination token from previous response |

### Cancel Task

Cancel an in-progress task by ID.

### Get Agent Card

Discover the agent's identity, skills, capabilities, and supported input/output modes.

### Push Notification Operations

| Operation | Description |
|---|---|
| Create Push Notification Config | Set up webhook notifications for task updates |
| Get Push Notification Config | Retrieve a config by task ID and config ID |
| List Push Notification Configs | List all configs for a task |
| Delete Push Notification Config | Remove a push notification config |

## MCP Tools (Copilot Studio)

When added to a Copilot Studio agent via the MCP endpoint, 6 tools are available:

| Tool | Description |
|---|---|
| `send_message` | Sync delegation — send message, wait for response |
| `send_message_async` | Fire-and-forget — returns task ID |
| `get_task` | Poll task status and artifacts |
| `list_tasks` | Filter tasks by context or state |
| `cancel_task` | Cancel in-progress task |
| `get_agent_card` | Runtime agent discovery |

## Use Cases

### Scheduled Intelligence (Power Automate)

Every Monday morning, ask Work IQ "summarize last week's key decisions and action items across my team" and post the result to a Teams channel.

### Event-Driven Routing (Power Automate)

When a support ticket escalates, send the ticket details to an A2A triage agent, parse the response, and route to the appropriate team.

### Selective Tool Use (Copilot Studio)

Your Copilot Studio agent calls `send_message` to ask Work IQ about a customer, inspects the response, combines it with CRM data, then presents a unified briefing — without handing off the entire conversation.

### Multi-Agent Fan-Out (Copilot Studio)

Ask three different A2A agents the same question, compare their responses, and synthesize a combined answer.

## Multi-Turn Conversations

Pass the `contextId` from a response into the next Send Message call to maintain conversation state. The agent remembers prior context.

## Targeting Other Agents

To connect to a non-Work IQ A2A agent:

1. Edit `script.csx` — update `A2A_ENDPOINT` to your agent's URL
2. Edit `script.csx` — set `A2A_PROTOCOL_BINDING` to `"httpjson"` if your agent uses the REST binding
3. Edit `apiProperties.json` — update OAuth settings to match your agent's auth requirements

## Configuration Constants (script.csx)

| Constant | Default | Description |
|---|---|---|
| `A2A_ENDPOINT` | `https://workiq.svc.cloud.microsoft/a2a/` | Agent endpoint URL |
| `A2A_PROTOCOL_BINDING` | `jsonrpc` | `jsonrpc` or `httpjson` |
| `A2A_VERSION` | `1.0` | A2A protocol version |
| `A2A_DEFAULT_AGENT_ID` | (empty) | Default agent ID for multi-agent gateways |
| `A2A_TENANT` | (empty) | Multi-tenant endpoint prefix |
| `APP_INSIGHTS_CONNECTION_STRING` | (empty) | Application Insights connection string |

## Troubleshooting

| Issue | Cause | Fix |
|---|---|---|
| 401 Unauthorized | Token audience mismatch | Ensure `resourceUri` in apiProperties.json matches `api://workiq.svc.cloud.microsoft` |
| 403 Forbidden (no scope) | Missing Copilot license | Assign Microsoft 365 Copilot license, wait 15-30 min |
| 403 with scope error | Missing admin consent | Grant admin consent for `WorkIQAgent.Ask` |
| Empty responses | Index not built | Wait 15-30 min after license assignment |
| Method not found (-32601) | Missing A2A-Version header | Verify the `setHeader` policy in apiProperties.json sets `A2A-Version: 1.0` |

## Links

- [A2A Protocol](https://a2a-protocol.org/latest/)
- [Work IQ API Overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-api-overview)
- [Work IQ API Quickstart](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-api-quickstart)
- [Work IQ Samples](https://github.com/microsoft/work-iq-samples)
- [Work IQ CLI](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-cli)
- [Work IQ API Terms of Use](https://learn.microsoft.com/en-us/legal/work-iq-apis/terms-of-use)
