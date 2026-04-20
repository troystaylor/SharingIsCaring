# HVE Guidance

HVE Guidance is a Copilot Studio custom connector that exposes curated AI engineering operations over the `microsoft/hve-core` repository using Model Context Protocol (MCP).

## What V1 Includes

This connector exposes 12 MCP tools through `InvokeMCP`:

1. `list_assets`
2. `get_asset`
3. `search_assets`
4. `recommend_assets_for_task`
5. `get_workflow_for_scenario`
6. `validate_instruction`
7. `validate_prompt`
8. `validate_agent_config`
9. `summarize_asset_changes`
10. `compare_asset_versions`
11. `generate_adoption_plan`
12. `get_release_highlights`

## Result Quality Features

- Asset responses include parsed frontmatter metadata when available.
- `get_asset` returns intended use, when not to use, key constraints, and related assets.
- `recommend_assets_for_task` scores candidates using task intent labels, metadata, and summary matching instead of plain path matching.
- Validation tools check for stronger structure and safety signals, not just basic length checks.

## Runtime Reliability Features

- GitHub reads use short-lived in-memory caching to reduce duplicate lookups and lower latency.
- GitHub throttling and API failures return structured retry metadata when available.
- Validation tools return actionable findings with why each issue matters and how to fix it.

## Evaluation Pack

See `docs/evaluation-scenarios.md` for the manual scenario-based evaluation pack you can run in Copilot Studio or the connector test pane.

## Files

- `apiDefinition.swagger.json`
- `apiProperties.json`
- `script.csx`
- `readme.md`

## Authentication

Connection parameters:

- `GitHub Token` (required)
- `Repository Owner` (optional, default `microsoft`)
- `Repository Name` (optional, default `hve-core`)
- `Branch` (optional, default `main`)

Recommended token scope for v1 is read-only repository access.

## Deploy

### PAC CLI

```powershell
pac auth create --environment "https://yourorg.crm.dynamics.com"

pac connector create \
  --api-definition-file apiDefinition.swagger.json \
  --api-properties-file apiProperties.json \
  --script-file script.csx
```

Update flow:

```powershell
pac connector update \
  --connector-id <CONNECTOR_ID> \
  --api-definition-file apiDefinition.swagger.json \
  --api-properties-file apiProperties.json \
  --script-file script.csx
```

If `--script-file` fails in your environment, upload `script.csx` manually in the connector Code tab after create.

## MCP Smoke Tests

### initialize

```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "initialize",
  "params": {
    "protocolVersion": "2025-11-25",
    "capabilities": {},
    "clientInfo": {
      "name": "Copilot Studio",
      "version": "1.0.0"
    }
  }
}
```

### tools/list

```json
{
  "jsonrpc": "2.0",
  "id": "2",
  "method": "tools/list",
  "params": {}
}
```

### tools/call (`list_assets`)

```json
{
  "jsonrpc": "2.0",
  "id": "3",
  "method": "tools/call",
  "params": {
    "name": "list_assets",
    "arguments": {
      "type": "instruction",
      "recursive": true,
      "maxItems": 25
    }
  }
}
```

## Notes

- `InvokeMCP` has no explicit body parameter by design for `mcp-streamable-1.0`.
- Telemetry uses a hardcoded Application Insights key placeholder in `script.csx` and silently skips if not configured.
- Tool errors are returned as MCP tool results with `isError=true`.
