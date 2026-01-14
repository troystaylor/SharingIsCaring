# Dataverse Power Orchestration Tools

## Overview

Dataverse Power Orchestration Tools is a Power Platform custom connector that exposes comprehensive Dataverse operations as MCP (Model Context Protocol) tools for Copilot Studio agents. It provides a complete tool server with 49 tools across 11 categories, plus 4 orchestration tools for tool discovery, execution, workflow orchestration, and pattern learning—all without external servers.

## Key Features
- **MCP Tool Server**: Exposes 45+ Dataverse tools + 4 orchestration tools via Model Context Protocol for Copilot Studio
- **Dynamic Tool Loading**: Tool definitions stored in agents.md in Dataverse table, loaded at runtime
- **Auto-Discovery (v1.0+)**: Automatically discovers Dataverse tables, Custom APIs, and Actions/Functions with Dataverse-backed caching (no Power Automate required)
- **4 Orchestration Tools**: 
  - `discover_functions` — Find available tools by intent/keywords/category
  - `invoke_tool` — Execute a specific tool dynamically
  - `orchestrate_plan` — Coordinate multi-step workflows with variable substitution
  - `learn_patterns` — Retrieve organizational learning from successful executions
- **Dual-Mode Operations**: MCP endpoint for Copilot Studio + typed query operations with IntelliSense
- **Self-Learning**: Agent logs successful patterns to Dataverse for continuous improvement
- **Dynamic Schema Support**: Power Automate IntelliSense for table columns via x-ms-dynamic-schema
- **OAuth-Only Authentication**: Direct AAD v2 authentication with Dataverse (no environment variables)
- **Dataverse Web API v9.2 Compliant**: OData 4.0 headers, optimistic concurrency, formatted values
- **No External Hosting**: Entire tool server runs in the connector runtime

## Tools by Category

### ORCHESTRATION (4 tools)
- `discover_functions` — Find available tools/resources/prompts
- `invoke_tool` — Trigger a specific tool
- `orchestrate_plan` — Coordinate multi-step operations
- `learn_patterns` — Upsert from retrieved Dataverse record

### READ Operations (7 tools)
### WRITE Operations (4 tools)
### BULK Operations (3 tools)
### RELATIONSHIPS (2 tools)
### METADATA Discovery (6 tools)
### ATTACHMENTS (2 tools)
### CHANGE TRACKING (1 tool)
### ASYNC Operations (2 tools)
### OWNERSHIP & SECURITY (7 tools)
### RECORD MANAGEMENT (4 tools)
### ADVANCED (7 tools)

## Deployment

1. Navigate to [Power Platform maker portal](https://make.powerapps.com)
2. Select target environment
3. **Data** → **Custom connectors** → **New custom connector** → **Import an OpenAPI file**
4. Upload `apiDefinition.swagger.json`
5. On **Code** tab, enable custom code
6. Paste contents of `script.csx`
7. **Create connector**
8. Test connection with OAuth flow

## Dynamic Instructions Table

Create `tst_agentinstructions` table in Dataverse with:

### Required Fields
- `tst_name` (Text) — Instruction set identifier (e.g., "dataverse-tools-agent")
- `tst_agentmd` (Multiline Text) — Main AGENTS.md content with tool definitions
- `tst_enabled` (Yes/No) — Enable/disable flag

### Learning & Pattern Storage
- `tst_learnedpatterns` (Multiline Text) — Auto-populated successful patterns (max 50 recent)
- `tst_version` (Text) — Version tracking
- `tst_updatecount` (Whole Number) — Number of pattern updates
- `tst_lastupdated` (DateTime) — Last pattern update timestamp

### Discovery Cache (v1.0+)
- `tst_discoveredtools` (Multiline Text, 1M chars) — JSON array of auto-discovered tools (tables, Custom APIs, Actions/Functions)
- `tst_discoverycache_timestamp` (DateTime) — Last discovery execution timestamp
- `tst_discoverycache_duration` (Whole Number) — Cache TTL in minutes (default: 30)

**Discovery Cache Behavior**: When `tools/list` is called, the script checks if cache has expired (`timestamp + duration < now`). If expired, it discovers all Dataverse tables, Custom APIs, and Actions/Functions, stores them in `tst_discoveredtools`, and updates the timestamp. This provides shared discovery cache across all connector instances without requiring Power Automate.

**Manual Cache Refresh**: Admins can force immediate cache refresh by editing fields in the `tst_agentinstructions` record:
- **Option 1**: Set `tst_discoverycache_timestamp` to 1+ hours ago (backdates expiry)
- **Option 2**: Set `tst_discoverycache_duration` to `0` (forces immediate expiry)
- **Option 3**: Clear `tst_discoverycache_timestamp` (nulls the timestamp)

Next `tools/list` call will automatically trigger re-discovery.

### Discovery Configuration (v1.0+)
- `tst_enabletables` (Yes/No, default: Yes) — Enable automatic discovery of Dataverse tables (standard + custom)
- `tst_enablecustomapis` (Yes/No, default: Yes) — Enable automatic discovery of Custom APIs
- `tst_enableactions` (Yes/No, default: Yes) — Enable automatic discovery of Actions/Functions
- `tst_discoveryblacklist` (Multiline Text) — Comma or newline-separated list of table/API names to exclude from discovery

**Example blacklist**: `systemuser, audit, activitypointer, asyncoperation`

See [agents.md](agents.md) for the tool definition format.

---

**Version**: 2.0.0  
**Brand Color**: #da3b01  
**MCP Tools**: 49 (45 Dataverse + 4 orchestration)
