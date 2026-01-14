# Dataverse Power Orchestration Tools

## Overview

Dataverse Power Orchestration Tools is a Power Platform custom connector that exposes comprehensive Dataverse operations as MCP (Model Context Protocol) tools for Copilot Studio agents. It provides a complete tool server with 49 tools across 11 categories, plus 4 orchestration tools for tool discovery, execution, workflow orchestration, and pattern learning—all without external servers.

## Key Features
- **MCP Tool Server**: Exposes 45 Dataverse tools + 4 orchestration tools via Model Context Protocol for Copilot Studio
- **Dynamic Tool Loading**: Tool definitions stored in agents.md in Dataverse table, loaded at runtime
- **4 Orchestration Tools**: 
  - `discover_functions` — Find available tools/resources/prompts
  - `invoke_tool` — Trigger a specific tool
  - `orchestrate_plan` — Coordinate multi-step operations
  - `learn_patterns` — Upsert from retrieved Dataverse record
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
- `tst_name` — Instruction set identifier (e.g., "dataverse-tools-agent")
- `tst_agentmd` — Main AGENTS.md content with tool definitions
- `tst_learnedpatterns` — Auto-populated successful patterns
- `tst_enabled` — Enable/disable flag

See [agents.md](agents.md) for the tool definition format.

---

**Version**: 2.0.0  
**Brand Color**: #da3b01  
**MCP Tools**: 49 (45 Dataverse + 4 orchestration)
