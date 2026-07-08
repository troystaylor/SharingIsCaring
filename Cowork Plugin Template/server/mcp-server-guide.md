# MCP Server Design Guide

Your remote MCP server is what Cowork calls through the `agentConnectors`
configuration. This guide covers the protocol requirements, tool design
patterns, response format, and constraints specific to Cowork.

## Protocol Requirements

| Requirement | Detail |
|-------------|--------|
| **Transport** | Streamable HTTP (HTTPS required, TLS 1.2+) |
| **Protocol** | JSON-RPC 2.0 message format |
| **Tool discovery** | Must support `tools/list` (recommended) |
| **Tool execution** | Must support `tools/call` |
| **Response time** | **< 30 seconds per tool call** (hard limit) |
| **Availability** | 99.9% uptime SLA recommended for store-published plugins |

## Endpoint

Your MCP server exposes a single HTTPS endpoint (e.g., `https://api.example.com/mcp`)
that accepts JSON-RPC 2.0 POST requests. Cowork sends three types of requests:

### initialize

```json
{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "initialize",
    "params": {
        "protocolVersion": "2025-03-26",
        "capabilities": {},
        "clientInfo": { "name": "cowork", "version": "1.0" }
    }
}
```

Response:

```json
{
    "jsonrpc": "2.0",
    "id": 1,
    "result": {
        "protocolVersion": "2025-03-26",
        "capabilities": { "tools": {} },
        "serverInfo": {
            "name": "{{service-name}}-mcp",
            "version": "1.0.0"
        }
    }
}
```

### tools/list

Returns the tools available on this server. Cowork uses this for dynamic
tool discovery — the agent reads tool names and descriptions to decide which
tools to call.

```json
{
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/list",
    "params": {}
}
```

Response:

```json
{
    "jsonrpc": "2.0",
    "id": 2,
    "result": {
        "tools": [
            {
                "name": "search_tickets",
                "description": "Search for support tickets by keyword, status, priority, assignee, or date range. Returns matching tickets with key fields.",
                "inputSchema": {
                    "type": "object",
                    "properties": {
                        "query": {
                            "type": "string",
                            "description": "Free text search across ticket title and description"
                        },
                        "status": {
                            "type": "string",
                            "enum": ["open", "in_progress", "resolved", "closed"],
                            "description": "Filter by ticket status"
                        },
                        "priority": {
                            "type": "string",
                            "enum": ["critical", "high", "medium", "low"],
                            "description": "Filter by priority level"
                        },
                        "assignee": {
                            "type": "string",
                            "description": "Filter by assignee email address"
                        },
                        "created_after": {
                            "type": "string",
                            "format": "date",
                            "description": "Only return tickets created after this date (ISO 8601)"
                        },
                        "created_before": {
                            "type": "string",
                            "format": "date",
                            "description": "Only return tickets created before this date (ISO 8601)"
                        },
                        "limit": {
                            "type": "integer",
                            "default": 25,
                            "maximum": 200,
                            "description": "Maximum number of results to return"
                        }
                    }
                }
            },
            {
                "name": "get_ticket",
                "description": "Get full details of a specific support ticket by ID, including description, comments, and history.",
                "inputSchema": {
                    "type": "object",
                    "properties": {
                        "ticket_id": {
                            "type": "string",
                            "description": "The ticket identifier (e.g., TKT-4421)"
                        }
                    },
                    "required": ["ticket_id"]
                }
            },
            {
                "name": "create_ticket",
                "description": "Create a new support ticket. Returns the created ticket with its assigned ID.",
                "inputSchema": {
                    "type": "object",
                    "properties": {
                        "title": {
                            "type": "string",
                            "description": "Short description of the issue"
                        },
                        "description": {
                            "type": "string",
                            "description": "Detailed description of the issue"
                        },
                        "priority": {
                            "type": "string",
                            "enum": ["critical", "high", "medium", "low"],
                            "description": "Priority level (defaults to medium if not specified)"
                        },
                        "category": {
                            "type": "string",
                            "enum": ["bug", "feature_request", "question", "billing", "access"],
                            "description": "Ticket category"
                        },
                        "assignee": {
                            "type": "string",
                            "description": "Email address of the person to assign this ticket to"
                        }
                    },
                    "required": ["title"]
                }
            },
            {
                "name": "update_ticket",
                "description": "Update fields on an existing support ticket. Only include fields you want to change.",
                "inputSchema": {
                    "type": "object",
                    "properties": {
                        "ticket_id": {
                            "type": "string",
                            "description": "The ticket identifier to update"
                        },
                        "status": {
                            "type": "string",
                            "enum": ["open", "in_progress", "resolved", "closed"],
                            "description": "New status for the ticket"
                        },
                        "priority": {
                            "type": "string",
                            "enum": ["critical", "high", "medium", "low"],
                            "description": "New priority level"
                        },
                        "assignee": {
                            "type": "string",
                            "description": "New assignee email address"
                        },
                        "comment": {
                            "type": "string",
                            "description": "Add a comment to the ticket along with the update"
                        }
                    },
                    "required": ["ticket_id"]
                }
            }
        ]
    }
}
```

### tools/call

Executes a specific tool. Cowork sends the tool name and arguments.

```json
{
    "jsonrpc": "2.0",
    "id": 3,
    "method": "tools/call",
    "params": {
        "name": "search_tickets",
        "arguments": {
            "status": "open",
            "priority": "high",
            "limit": 10
        }
    }
}
```

## Response Format

### Successful response

Return structured JSON that the agent can format for the user. Include enough
context for the skill to present a useful summary without needing follow-up calls.

```json
{
    "jsonrpc": "2.0",
    "id": 3,
    "result": {
        "content": [
            {
                "type": "text",
                "text": "{\"total_count\":3,\"has_more\":false,\"tickets\":[{\"id\":\"TKT-4421\",\"title\":\"Login page returns 500 error\",\"status\":\"open\",\"priority\":\"high\",\"assignee\":\"jamie@zava.com\",\"created_at\":\"2026-05-01T09:15:00Z\",\"category\":\"bug\"},{\"id\":\"TKT-4418\",\"title\":\"API timeout on bulk export\",\"status\":\"open\",\"priority\":\"high\",\"assignee\":\"alex@zava.com\",\"created_at\":\"2026-04-30T14:22:00Z\",\"category\":\"bug\"},{\"id\":\"TKT-4415\",\"title\":\"Billing discrepancy Q2 invoice\",\"status\":\"open\",\"priority\":\"high\",\"assignee\":null,\"created_at\":\"2026-04-29T11:05:00Z\",\"category\":\"billing\"}]}"
            }
        ]
    }
}
```

### Error response

Return errors as `isError: true` with a clear message. Do NOT use JSON-RPC
error codes for business logic errors — those are for protocol-level failures.

```json
{
    "jsonrpc": "2.0",
    "id": 3,
    "result": {
        "content": [
            {
                "type": "text",
                "text": "{\"error\":\"not_found\",\"message\":\"Ticket TKT-9999 does not exist\",\"suggestion\":\"Check the ticket ID or search by title\"}"
            }
        ],
        "isError": true
    }
}
```

Use JSON-RPC errors only for protocol failures:

```json
{
    "jsonrpc": "2.0",
    "id": 3,
    "error": {
        "code": -32601,
        "message": "Method not found"
    }
}
```

### Response design principles

1. **Return structured JSON, not prose.** The skill formats it for the user.
   `{"total_count": 3, "tickets": [...]}` is better than `"Found 3 tickets..."`.

2. **Include summary counts.** `total_count`, `has_more`, and pagination cursors
   let the skill tell the user "showing 25 of 142 results" without guessing.

3. **Include navigable identifiers.** Return IDs and URLs so the skill can
   offer "want me to pull details on TKT-4421?" or link to the record.

4. **Return only relevant fields.** Search results should return summary
   fields (id, title, status, assignee). Detail endpoints return everything.
   Don't return the full description in a search result — it wastes the
   context window.

5. **Include action context.** For status fields, include what transitions
   are valid. For assignee fields, indicate if reassignment is allowed.
   This saves the skill from needing a separate validation call.

## Tool Design Patterns

### Small APIs (< 15 operations)

One tool per action. Name each tool clearly:

```
search_tickets      — Find tickets by criteria
get_ticket          — Get full details of one ticket
create_ticket       — Create a new ticket
update_ticket       — Update an existing ticket
search_customers    — Find customers
get_customer        — Get customer details
```

### Large APIs (50+ operations)

Use a **search + execute** pattern to stay within the 20-tool practical limit:

```
search_actions      — "What can I do?" Returns a filtered list of available actions
execute_action      — Runs a specific action by name with provided parameters
```

The `search_actions` tool returns action metadata including parameter schemas,
so the agent knows how to call `execute_action` correctly.

### Tool naming rules

- **Use snake_case.** `search_tickets` not `searchTickets` or `SearchTickets`.
- **Verb first.** `get_ticket`, `create_ticket`, `update_ticket`, `delete_ticket`.
- **Be specific.** `get_ticket_comments` not `getData`.
- **Match the skill's expectations.** If your skill references `search_tickets`,
  the tool must be named exactly `search_tickets`.

### Input schema requirements

**Every parameter must have a `description`.** This is what the agent reads
to decide what values to pass. Vague or missing descriptions cause the agent
to guess — badly.

```json
// Bad — agent doesn't know what format to use
"created_after": { "type": "string" }

// Good — agent knows exactly what to send
"created_after": {
    "type": "string",
    "format": "date",
    "description": "Only return tickets created after this date (ISO 8601, e.g., 2026-04-01)"
}
```

Use `enum` for fields with fixed valid values. This prevents invalid inputs
at the schema level instead of failing at runtime.

Use `required` to mark mandatory fields. Don't make the agent guess which
fields are optional.

## The 30-Second Rule

Cowork enforces a **30-second timeout per tool call**. If your API or backend
processing takes longer, you must work around it:

| Approach | When to use |
|----------|-------------|
| **Pagination** | Queries that could return large datasets — limit default page size and let the agent request more pages |
| **Pre-aggregation** | Report queries — compute summaries server-side instead of returning raw records for the agent to count |
| **Async with polling** | Long-running operations (report generation, bulk updates) — return a job ID immediately, provide a `check_job_status` tool |

### Async pattern example

```
# Tool 1: Start the operation
generate_report(period: "2026-Q1", group_by: "category")
→ { "job_id": "rpt-8812", "status": "running", "estimated_seconds": 45 }

# Tool 2: Check status (skill polls this)
check_job_status(job_id: "rpt-8812")
→ { "job_id": "rpt-8812", "status": "complete", "result_url": "..." }

# Tool 3: Retrieve results
get_report(job_id: "rpt-8812")
→ { "report": { ... } }
```

The skill tells the user "generating your report, this may take a moment"
and calls `check_job_status` until it completes.

## Authentication Passthrough

When Cowork calls your MCP endpoint, it passes the user's OAuth token (acquired
during the one-time sign-in flow) in the `Authorization` header. Your server
must:

1. **Validate the token** against your OAuth provider
2. **Scope data to the authenticated user** — never return data the user
   shouldn't see
3. **Return 401 for expired/invalid tokens** — Cowork will prompt the user
   to re-authenticate
4. **Never cache tokens** beyond the request lifecycle

## Hosting Options

Your MCP server can run anywhere that serves HTTPS:

| Platform | Good for | Notes |
|----------|----------|-------|
| **Azure Container Apps** | Production workloads | Managed scaling, managed identity, custom domains |
| **Azure Functions** | Lightweight APIs | Consumption plan for low-traffic, watch for cold start vs. 30s limit |
| **Azure App Service** | Full control | Always-on available, no cold start risk |
| **Microsoft Foundry Toolbox** | Teams already using Foundry | Managed MCP endpoint with centralized auth and versioning (see below) |
| **AWS Lambda + API Gateway** | AWS-native teams | Watch for cold start |
| **Any HTTPS server** | Self-hosted | Must handle TLS 1.2+, uptime, and scaling yourself |

**Cold start warning:** If using serverless (Functions, Lambda), cold starts
can eat into your 30-second budget. Use provisioned concurrency or always-on
plans for production plugins.

### Microsoft Foundry Toolbox (Preview)

If your team is already using [Microsoft Foundry](https://learn.microsoft.com/azure/ai-foundry/),
you can skip building a standalone MCP server entirely. A
[Foundry Toolbox](https://learn.microsoft.com/azure/foundry/agents/how-to/tools/toolbox)
bundles multiple tools into a single managed MCP-compatible endpoint.

**What a Toolbox provides:**

| Capability | Detail |
|------------|--------|
| **Managed MCP endpoint** | `{project_endpoint}/toolboxes/{name}/mcp?api-version=v1` — no infrastructure to deploy or maintain |
| **Centralized auth** | Credential injection, token refresh, and policy enforcement handled by the platform via Entra ID and OAuth |
| **Tool bundling** | Combine MCP servers, OpenAPI specs, Azure AI Search, web search, code interpreter, and A2A connections in one endpoint |
| **Versioning** | Create v2 of your toolbox, test it against the version-specific endpoint, promote to default — all consumers update without code changes |
| **Cross-runtime** | Consumable by Foundry Agent Service, Microsoft Agent Framework, LangGraph, GitHub Copilot SDK, Claude Code, and any MCP client |

**How to use with a Cowork plugin:**

1. Create a Foundry Toolbox with your API tools (MCP servers, OpenAPI specs, etc.)
2. Get the consumer endpoint: `{project_endpoint}/toolboxes/{name}/mcp?api-version=v1`
3. Set this URL as the `mcpServerUrl` in your plugin's `manifest.json`

**Current limitations:**

- **Preview status** — Toolboxes are in public preview. The API may change before GA.
- **Required header** — Every request to the Toolbox endpoint must include `Foundry-Features: Toolboxes=V1Preview`. Verify that Cowork's connector client passes this header; if not, you may need a thin proxy layer.
- **Auth model** — Toolboxes use Entra ID (managed identity or user passthrough), not the Plugin Vault OAuth/API Key model. This may require additional configuration to bridge with Cowork's `OAuthPluginVault` auth.
- **Azure dependency** — Requires an Azure Foundry project and Azure subscription.

**When to use Toolbox vs. self-hosted:**

| Scenario | Recommendation |
|----------|----------------|
| Already in Foundry, want to reuse existing tool configs | Toolbox |
| Need to bundle multiple APIs into one endpoint | Toolbox |
| No Azure subscription or Foundry project | Self-hosted (Container Apps, Functions, etc.) |
| Need full control over auth, caching, rate limiting | Self-hosted |
| Production plugin for the M365 App Store | Self-hosted (until Toolbox exits preview) |

## Tool Annotations and Confirmation Management

Cowork reads the standard MCP `annotations` object on tools returned from
`tools/list` and uses it to decide whether a tool call needs user confirmation
and what label to show.

### Available fields

| Field | Type | Effect |
|-------|------|--------|
| `readOnlyHint` | bool | `false` → confirmation required before the tool runs |
| `destructiveHint` | bool | `true` → confirmation required before the tool runs |
| `title` | string | Human-readable label shown on the confirmation dialog (falls back to tool name) |

### Confirmation rules

- Confirmation is required if `readOnlyHint == false` OR `destructiveHint == true`
- **All tools should have annotations.** Tools without annotations are treated
  as destructive and require confirmation.
- Safe read-only tools should set `"readOnlyHint": true` to auto-run without prompts

### Examples

A destructive action with a friendly label:

```json
{
    "name": "delete_ticket",
    "description": "Permanently delete a support ticket.",
    "annotations": {
        "title": "Delete Ticket",
        "destructiveHint": true
    },
    "inputSchema": { ... }
}
```

A safe read that auto-runs:

```json
{
    "name": "search_tickets",
    "description": "Search for support tickets.",
    "annotations": {
        "title": "Search Tickets",
        "readOnlyHint": true
    },
    "inputSchema": { ... }
}
```

A mutation that modifies data (default behavior — confirmation required):

```json
{
    "name": "update_ticket",
    "description": "Update fields on an existing ticket.",
    "annotations": {
        "title": "Update Ticket",
        "readOnlyHint": false
    },
    "inputSchema": { ... }
}
```

### Recommendation

Set `annotations` on every tool from day one. Even though annotation-driven
confirmation is being rolled out progressively for non-Microsoft MCP servers,
setting the hints now is forward-compatible — confirmation prompts will surface
as the rollout expands with no developer change required.

## Widget-Enabled Tools (MCP Apps)

Cowork supports rendering interactive UI widgets from your MCP server following
the [MCP Apps Extension (SEP-1865)](https://github.com/modelcontextprotocol/ext-apps).
A widget is an HTML/JS view rendered inline in the conversation inside a
sandboxed iframe.

### When to use widgets

Use widgets when you need a rich, interactive, or visual surface: searchable
pickers, dashboards, charts, previews, or live status. For simple confirmations
or short forms, prefer MCP elicitation instead.

### How it works

1. **Declare a UI resource on the tool**: Add `_meta.ui.resourceUri` (with a
   `ui://` scheme) to your tool's `tools/list` definition
2. **Tool handler returns data, not HTML**: The tool's `tools/call` response
   contains structured data that the widget renders
3. **Serve the HTML via `resources/read`**: Cowork fetches the HTML template
   from your server when the widget mounts

### Key constraints

| Constraint | Detail |
|------------|--------|
| `resourceUri` must use `ui://` scheme | Other schemes are ignored |
| URI max length | 1024 characters |
| MIME type | `text/html;profile=mcp-app` (set on `resources/read` response) |
| Serve HTML as `text` field | Cowork doesn't decode base64 `blob` bodies |
| Result size limit | `CallToolResult` payloads over 64 KiB aren't pushed to the widget |
| Widget callbacks | Only `resources/read`, `tools/call`, and `ui/message` are forwarded |
| Rate limit | 60 widget-initiated requests per minute per conversation |
| Display modes | `inline` and `fullscreen` only (`pip` not supported) |

### Widget tool example

```json
{
    "name": "show_ticket_dashboard",
    "description": "Show an interactive dashboard of ticket metrics.",
    "_meta": {
        "ui": { "resourceUri": "ui://your-app/ticket-dashboard.html" }
    },
    "inputSchema": { ... }
}
```

### Graceful degradation

Widget-enabled tools should always return meaningful text or `structuredContent`
from their handler. If widget rendering is unavailable, the tool still runs and
its data flows to the agent normally.

For full implementation details, see the
[MCP apps plugin author guide](https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/mcp-apps-support).

## Checklist

Before connecting your MCP server to a Cowork plugin:

- [ ] Endpoint accepts POST requests with `Content-Type: application/json`
- [ ] Responds to `initialize`, `tools/list`, and `tools/call` methods
- [ ] Returns valid JSON-RPC 2.0 responses for all methods
- [ ] Every tool has a descriptive `name` and `description`
- [ ] Every tool has `annotations` (readOnlyHint/destructiveHint) for confirmation management
- [ ] Every input parameter has a `description` in the schema
- [ ] Enum fields use `enum` in the schema (not just documented in text)
- [ ] Required fields are marked in the `required` array
- [ ] Search tools include `total_count` and `has_more` in responses
- [ ] Error responses use `isError: true` (not JSON-RPC error codes)
- [ ] All tool calls complete within 30 seconds
- [ ] Long operations use the async job pattern
- [ ] Auth token is validated on every request
- [ ] Data is scoped to the authenticated user
- [ ] HTTPS with TLS 1.2+ is enforced
- [ ] No secrets or credentials in response payloads
- [ ] (Optional) Widget tools declare `_meta.ui.resourceUri` and serve HTML via `resources/read`
- [ ] (Optional) Feedback tools (`record_skill_feedback`, `get_skill_insights`) implemented for skill iteration

## Skill Feedback Tools (Optional)

If you include the `improve-skills` skill in your plugin, your MCP server
needs two additional tools that capture runtime feedback and surface insights
for skill authors.

### record_skill_feedback

Records a single feedback event when a skill doesn't perform as expected.
The `improve-skills` skill calls this automatically when it detects a user
correction or missed activation.

**Storage:** Append to a JSONL file in blob storage, or insert into a table.
Each entry should include:
- `skill_name` — which skill the feedback is about
- `feedback_type` — `missed_activation`, `wrong_skill`, `poor_output`, `missing_feature`, or `trigger_phrase`
- `user_input` — the anonymized request pattern (no PII)
- `expected_behavior` — what should have happened
- `suggested_trigger` — a trigger phrase to add (for `trigger_phrase` type)
- `recorded_at` — ISO 8601 timestamp

**Privacy requirements:**
- Strip all PII before storing (names, emails, record IDs)
- Don't track which user submitted feedback
- Auto-delete entries older than 90 days
- Deduplicate: if the same `user_input` + `feedback_type` pair exists,
  increment a counter rather than creating a new entry

### get_skill_insights

Returns accumulated feedback grouped by skill and type. Used by the plugin
author during development to see what needs updating.

**Response format:**

```json
{
    "total_entries": 12,
    "since": "2026-04-01",
    "by_skill": {
        "search-and-explore": {
            "missed_activation": 5,
            "poor_output": 2,
            "trigger_phrase": 3,
            "top_suggestions": [
                { "phrase": "overdue tickets", "count": 3 },
                { "phrase": "past SLA", "count": 2 }
            ]
        },
        "report-and-summarize": {
            "wrong_skill": 1,
            "poor_output": 1,
            "top_suggestions": []
        }
    }
}
```

See `skills/improve-skills/SKILL.md` for the full skill template and
`skills/search-and-explore/references/skill-improvement.md` for the
complete iteration guide.
