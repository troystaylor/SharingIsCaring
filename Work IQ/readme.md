# Work IQ

Power Platform custom connector for the [Microsoft Work IQ A2A API](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq/api-overview). Ask natural language questions about workplace data and receive structured answers — Work IQ reasons across email, meetings, documents, Teams messages, and people context automatically.

## What it does

- **MCP endpoint** (`/mcp`) with `x-ms-agentic-protocol: mcp-streamable-1.0` — proxies to Work IQ's Remote MCP server for Copilot Studio
- **Typed operation** (`/a2a/`) — "Ask Work IQ" A2A action for Power Automate flows
- Conversational interface — ask questions in plain English about M365 data
- Multi-turn support via `contextId` for follow-up questions
- Permission-trimmed and policy-enforced: responses respect M365 permissions, sensitivity labels, and compliance policies
- No need to pick which workload has the answer — Work IQ routes automatically

## Example Queries

- "What meetings do I have today?"
- "Summarize recent discussions about project risks"
- "What did Dana share about the Q3 budget?"
- "Who is working on the Contoso proposal?"
- "What emails mention the deadline change?"

## Relationship to Agent 365 MCP Connector

| Connector | Approach | Best for |
|-----------|----------|----------|
| **Work IQ** (this) | Conversational — MCP proxy + A2A typed action | Copilot Studio agents and Power Automate flows needing workplace context without picking a workload |
| **Agent 365 MCP** | Tool-based — individual MCP servers (Mail, Calendar, Teams, etc.) | Copilot Studio agents that need governed tool access to specific M365 workloads |

Work IQ is simpler (unified intelligence, natural language in/out). Agent 365 MCP is more granular (11 servers, full tool schemas, programmatic control per workload).

## Copilot Studio Usage

- Add this connector to your agent; Copilot Studio detects the MCP endpoint at `/mcp`
- Agent can ask Work IQ questions via natural language — no tool selection needed
- Work IQ reasons across all M365 data (email, calendar, documents, Teams, people) automatically

## Prerequisites

1. **Azure AD App Registration**:
   - Redirect URI: `https://global.consent.azure-apim.net/redirect`
   - API permissions: `https://workiq.svc.cloud.microsoft/.default` (delegated)
   - For multitenant orgs: register app as `AzureADMultipleOrgs`
2. **Licensing**: Work IQ API usage is billed via [Copilot Credits](https://learn.microsoft.com/en-us/microsoft-365/copilot/usage-based-billing-overview-copilot-credits)

## Import & Deploy

1. Import via Maker portal → Custom connectors → Import OpenAPI (apiDefinition.swagger.json)
2. Security: Configure OAuth2 (AAD) with your app registration `clientId` and scope `https://workiq.svc.cloud.microsoft/.default`
3. Create a connection and test with a simple question

## Multi-Turn Conversations

To ask follow-up questions, pass the `contextId` from the previous response:

```
1. AskWorkIQ → question: "What meetings do I have today?"
   → response includes contextId: "ctx-1"

2. AskWorkIQ → question: "Tell me more about the 2 PM call", contextId: "ctx-1"
   → response uses conversation context from step 1
```

## Auth Notes

- **Delegated only** — runs in the context of the signed-in user
- **OBO (On-Behalf-Of) supported** — for service-to-service scenarios
- **No app-only auth** — application-only authentication is not supported
- **Multitenant**: Token issuer must match the user's home tenant. Register as `AzureADMultipleOrgs` for parent/child orgs
- **Location metadata**: Include `timeZone` and `timeZoneOffset` for time-sensitive queries (meetings, deadlines)

## Supported Data

Work IQ can reason over:
- Email messages
- Meetings and calendar data
- Documents in OneDrive and SharePoint
- Microsoft Teams messages
- People and organizational context
- Microsoft Planner plans
- Enterprise search results
