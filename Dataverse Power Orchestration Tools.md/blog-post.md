---
layout: post
title: "Beyond MCP: Building an Orchestration Layer for Dataverse"
date: 2026-01-15
categories: [power platform, copilot studio, mcp]
tags: [dataverse, mcp, copilot-studio, power-platform, ai-agents, orchestration]
---

# Beyond MCP: Building an Orchestration Layer for Dataverse in Copilot Studio

After building several MCP connectors—each exposing a handful of tools to Copilot Studio—I hit a wall. **Dataverse has 45 operations across 11 categories.** Simply wrapping them in MCP and handing them to an AI agent doesn't scale. The agent drowns in options, the context window bloats with tool definitions, and every request starts from zero.

This post introduces a different approach: **post-MCP orchestration**. Instead of just exposing tools, we add a layer that helps agents *discover*, *compose*, and *learn from* tool usage. The result is the **Dataverse Power Orchestration Tools** connector—45 Dataverse tools plus 4 orchestration tools that fundamentally change how agents interact with enterprise data.

## The Problem with "Just MCP"

Basic MCP connectors work great for focused domains—a Bookings connector with 8 tools, a Calendar connector with 12. But Dataverse is different:

- **Context window bloat**: 45 tool definitions consume thousands of tokens before the conversation starts
- **Selection paralysis**: `create_row` vs `upsert` vs `create_multiple`? The model guesses
- **No memory**: Every request rediscovers the same patterns
- **No composition**: Multi-step workflows require multiple round-trips

Anthropic's [Building Effective Agents](https://www.anthropic.com/engineering/building-effective-agents) post notes that "the most successful implementations use simple, composable patterns." The orchestration pattern solves this:

1. **Discovery first**: Agent uses `search_tools` to find relevant tools
2. **Dynamic execution**: Agent uses `call_tool` to invoke discovered tools
3. **Workflow composition**: Agent uses `execute_workflow` to chain operations
4. **Continuous learning**: Agent uses `get_patterns` to surface what worked before

This mirrors how expert developers work—they don't memorize every API; they search docs, try things, and build on past successes.

## The 4 Orchestration Tools

### `search_tools` — Discovery by Intent

Instead of browsing 45 tool definitions, the agent describes what it wants to accomplish:

```json
{
  "name": "search_tools",
  "arguments": {
    "intent": "create new customer account",
    "category": "WRITE",
    "maxResults": 5
  }
}
```

Returns matching tools with relevance scores:

```json
{
  "intent": "create new customer account",
  "matchCount": 3,
  "tools": [
    { "name": "dataverse_create_row", "category": "WRITE", "score": 18 },
    { "name": "dataverse_upsert", "category": "WRITE", "score": 10 },
    { "name": "dataverse_create_multiple", "category": "BULK", "score": 8 }
  ]
}
```

The scoring algorithm matches against:
- **Keywords** (exact match = 10 points)
- **Tool name** (contains word = 8 points)
- **Description** (contains word = 3 points)
- **Partial keyword match** (5 points)

### `call_tool` — Dynamic Execution

Once the agent finds the right tool, it executes through `call_tool`:

```json
{
  "name": "call_tool",
  "arguments": {
    "toolName": "dataverse_create_row",
    "args": {
      "table": "accounts",
      "record": { "name": "Contoso Ltd", "revenue": 500000 }
    }
  }
}
```

This indirection provides:
- **Validation**: Confirms tool exists before execution
- **Error handling**: Returns input schema if arguments are invalid
- **Logging**: Tracks which tools are actually used
- **Suggestions**: Guides agent to `search_tools` if tool not found

### `execute_workflow` — Multi-Step Sequences

Complex operations often require multiple tools in sequence. Instead of multiple round-trips:

```json
{
  "name": "execute_workflow",
  "arguments": {
    "steps": [
      {
        "name": "create_account",
        "tool": "dataverse_create_row",
        "args": { "table": "accounts", "record": { "name": "Contoso" } }
      },
      {
        "name": "create_contact",
        "tool": "dataverse_create_row",
        "args": {
          "table": "contacts",
          "record": {
            "firstname": "John",
            "lastname": "Smith",
            "parentcustomerid_account@odata.bind": "/accounts(${create_account.accountid})"
          }
        }
      }
    ],
    "stopOnError": true
  }
}
```

Key features:
- **Variable substitution**: `${stepName.property}` references previous results
- **Atomic execution**: `stopOnError: true` halts on first failure
- **Shared context**: Results accumulate for downstream steps

### `get_patterns` — Organizational Learning

The connector stores successful patterns in a Dataverse table (`tst_agentinstructions`). The `get_patterns` tool surfaces this learned knowledge:

```json
{
  "name": "get_patterns",
  "arguments": {
    "category": "WRITE",
    "keyword": "account"
  }
}
```

Returns patterns like:
- "When creating accounts, always include `accountnumber` for upsert operations"
- "Use `query_expand` instead of separate lookups for related contacts"
- "Filter by `statecode eq 0` for active records only"

## The CLAUDE.md Pattern for Dataverse

Anthropic's [Claude Code Best Practices](https://www.anthropic.com/engineering/claude-code-best-practices) introduced `CLAUDE.md` files—special context files that Claude automatically reads. I've adapted this for Power Platform:

Instead of filesystem context, this connector stores its `agents.md` content in a **Dataverse table**:

| Column | Purpose |
|--------|---------|
| `tst_name` | Instruction set identifier (e.g., "dataverse-tools-agent") |
| `tst_agentmd` | Tool definitions, selection guidance, organization context |
| `tst_learnedpatterns` | Auto-populated successful patterns |
| `tst_version` | Version tracking |
| `tst_enabled` | Toggle instruction sets |

### Dynamic Loading

At request time, the connector queries Dataverse and merges static + dynamic + learned context:

```csharp
private async Task<string> GetAgentMdAsync()
{
    // Return cached if available (per-request lifecycle)
    if (_cachedAgentMd != null) return _cachedAgentMd;
    
    // Query tst_agentinstructions table for active instructions
    var filter = "tst_name eq 'dataverse-tools-agent' and tst_enabled eq true";
    var url = BuildDataverseUrl($"tst_agentinstructionses?$filter={filter}&$top=1");
    
    var result = await SendDataverseRequest(HttpMethod.Get, url, null);
    var record = (result["value"] as JArray)?.FirstOrDefault() as JObject;
    
    _cachedInstructionsRecordId = record?["tst_agentinstructionsid"]?.ToString();
    var agentMd = record?["tst_agentmd"]?.ToString() ?? "";
    var learnedPatterns = record?["tst_learnedpatterns"]?.ToString();
    
    // Append learned patterns if present
    if (!string.IsNullOrWhiteSpace(learnedPatterns))
        agentMd += "\n\n## LEARNED PATTERNS\n\n" + learnedPatterns;
    
    _cachedAgentMd = agentMd;
    return _cachedAgentMd;
}
```

Tools are then parsed from a `## TOOLS` JSON block in the markdown and merged with the 4 orchestration tools:

```csharp
private JArray ParseToolsFromAgentMd(string agentMd)
{
    var toolsMarker = "## TOOLS";
    var toolsIndex = agentMd.IndexOf(toolsMarker);
    if (toolsIndex < 0) return new JArray();
    
    // Find JSON array after marker
    var afterMarker = agentMd.Substring(toolsIndex + toolsMarker.Length);
    var jsonStart = afterMarker.IndexOf('[');
    var jsonEnd = FindMatchingBracket(afterMarker, jsonStart);
    
    var tools = JArray.Parse(afterMarker.Substring(jsonStart, jsonEnd - jsonStart + 1));
    
    // Always inject orchestration tools first
    var mcpTools = GetOrchestrationToolDefinitions();
    foreach (var tool in tools)
        mcpTools.Add(ConvertToMcpFormat(tool as JObject));
    
    return mcpTools;
}
```

### Benefits Over Static Tools

| Static Tools | Orchestration Tools |
|--------------|---------------------|
| Fixed at deployment | Updated without redeploying |
| Same for all environments | Per-environment customization |
| No memory between requests | Learned patterns accumulate |
| All tools always visible | Relevant tools surfaced dynamically |

## The 45 Dataverse Tools

The orchestration layer sits atop a comprehensive Dataverse toolset organized into **11 categories**:

| Category | Tools | Examples |
|----------|-------|----------|
| **READ** | 7 | `list_rows`, `get_row`, `query_expand`, `fetchxml`, `count_rows`, `aggregate` |
| **WRITE** | 4 | `create_row`, `update_row`, `delete_row`, `upsert` |
| **BULK** | 4 | `create_multiple`, `update_multiple`, `upsert_multiple`, `batch` |
| **RELATIONSHIPS** | 2 | `associate`, `disassociate` |
| **METADATA** | 6 | `get_entity_metadata`, `get_attribute_metadata`, `get_relationships` |
| **SECURITY** | 8 | `whoami`, `assign`, `share`, `unshare`, `retrieve_principal_access` |
| **RECORD_MGMT** | 4 | `set_state`, `merge`, `initialize_from`, `calculate_rollup` |
| **ATTACHMENTS** | 2 | `upload_attachment`, `download_attachment` |
| **CHANGE_TRACKING** | 1 | `track_changes` |
| **ASYNC** | 2 | `get_async_operation`, `list_async_operations` |
| **ADVANCED** | 5 | `execute_action`, `execute_function`, `detect_duplicates`, `get_audit_history` |

Each tool includes **category** and **keywords** metadata that `search_tools` uses for relevance matching. Full tool definitions with input schemas are in the [agents.md](https://github.com/troystaylor/SharingIsCaring) file.

## In Practice

### Discovery → Execution Flow

```
User: "I need to create a new customer with a primary contact"

Agent: [calls search_tools with intent="create customer contact"]
       Found: dataverse_create_row (score: 18), dataverse_upsert (score: 10)

Agent: [calls execute_workflow]
       Step 1: dataverse_create_row → accounts → {accountid: "abc-123"}
       Step 2: dataverse_create_row → contacts → uses {{step1.accountid}}

Agent: "Created Contoso Ltd with John Smith as primary contact."

[Connector logs pattern: "workflow: create_row → create_row"]
```

### Pattern-Based Guidance

```
User: "What's the best way to handle account creation?"

Agent: [calls get_patterns with category="WRITE", keyword="account"]
       Returns: 3 learned patterns from organizational history

Agent: "Based on how your organization uses this connector:
       1. Include accountnumber for deduplication via upsert
       2. Set customertypecode to distinguish prospects vs customers  
       3. Always link to parent account if part of hierarchy"
```

## Technical Implementation

### Architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│                         Copilot Studio Agent                         │
└──────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼ MCP JSON-RPC
┌──────────────────────────────────────────────────────────────────────┐
│                    Orchestration Layer (4 tools)                     │
│  ┌─────────────┐ ┌─────────────┐ ┌────────────────┐ ┌─────────────┐  │
│  │search_tools │ │ call_tool   │ │execute_workflow│ │get_patterns │  │
│  └─────────────┘ └─────────────┘ └────────────────┘ └─────────────┘  │
└──────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼ Dictionary Dispatch
┌──────────────────────────────────────────────────────────────────────┐
│                     Dataverse Tools (45 tools)                       │
│  READ │ WRITE │ BULK │ RELATIONSHIPS │ METADATA │ SECURITY │ ...    │
└──────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼ OData 4.0
┌──────────────────────────────────────────────────────────────────────┐
│                      Dataverse Web API v9.2                          │
└──────────────────────────────────────────────────────────────────────┘
```

### Dictionary-Based Tool Dispatch

Tools dispatch through O(1) dictionary lookup—no switch statements:

```csharp
private Dictionary<string, Func<JObject, Task<JObject>>> _toolHandlers;

// Registration at initialization
_toolHandlers = new Dictionary<string, Func<JObject, Task<JObject>>>
{
    // Orchestration tools
    [TOOL_SEARCH_TOOLS] = ExecuteSearchTools,
    [TOOL_CALL_TOOL] = ExecuteCallTool,
    [TOOL_EXECUTE_WORKFLOW] = ExecuteWorkflow,
    [TOOL_GET_PATTERNS] = ExecuteGetPatterns,
    
    // Dataverse tools (45 more)
    [TOOL_LIST_ROWS] = ExecuteListRows,
    [TOOL_GET_ROW] = ExecuteGetRow,
    [TOOL_CREATE_ROW] = ExecuteCreateRow,
    // ...
};

// Dispatch
private async Task<JObject> ExecuteToolByName(string toolName, JObject args)
{
    if (!_toolHandlers.TryGetValue(toolName, out var handler))
        throw new ArgumentException($"Unknown tool: {toolName}");
    
    return await handler(args).ConfigureAwait(false);
}
```

### Relevance Scoring Algorithm

`search_tools` scores tools against intent keywords:

```csharp
private async Task<JObject> ExecuteSearchTools(JObject args)
{
    var intent = args["intent"]?.ToString()?.ToLowerInvariant() ?? "";
    var category = args["category"]?.ToString()?.ToLowerInvariant();
    var intentWords = intent.Split(new[] { ' ', ',', '-', '_' }, 
                                   StringSplitOptions.RemoveEmptyEntries);
    
    var matches = new List<(JObject tool, int score)>();
    
    foreach (var tool in await GetFullToolsAsync())
    {
        var name = tool["name"]?.ToString()?.ToLowerInvariant() ?? "";
        var desc = tool["description"]?.ToString()?.ToLowerInvariant() ?? "";
        var keywords = tool["keywords"] as JArray;
        var keywordList = keywords?.Select(k => k.ToString().ToLowerInvariant()).ToList();
        
        // Category filter (exact match)
        if (!string.IsNullOrWhiteSpace(category) && 
            tool["category"]?.ToString()?.ToLowerInvariant() != category)
            continue;
        
        var score = 0;
        foreach (var word in intentWords)
        {
            if (word.Length < 2) continue;
            
            if (keywordList.Contains(word)) score += 10;           // Exact keyword
            else if (name.Contains(word)) score += 8;               // Name match
            else if (desc.Contains(word)) score += 3;               // Description
            else if (keywordList.Any(k => k.Contains(word))) 
                score += 5;                                          // Partial keyword
        }
        
        if (score > 0) matches.Add((tool, score));
    }
    
    return new JObject
    {
        ["tools"] = new JArray(matches.OrderByDescending(m => m.score)
                                      .Take(maxResults)
                                      .Select(m => new JObject { ... }))
    };
}
```

### Workflow Variable Substitution

`execute_workflow` uses `{{variable.path}}` syntax to reference previous step results:

```csharp
private JObject ResolveWorkflowVariables(JObject args, JObject context)
{
    var resolved = new JObject();
    
    foreach (var prop in args.Properties())
        resolved[prop.Name] = ResolveValue(prop.Value, context);
    
    return resolved;
}

private JToken ResolveValue(JToken value, JObject context)
{
    if (value.Type == JTokenType.String)
    {
        var str = value.ToString();
        // Check for {{varName}} or {{varName.property}} pattern
        if (str.StartsWith("{{") && str.EndsWith("}}"))
        {
            var varPath = str.Substring(2, str.Length - 4).Trim();
            return ResolveVariablePath(varPath, context);
        }
    }
    else if (value.Type == JTokenType.Object)
        return ResolveWorkflowVariables(value as JObject, context);
    
    return value;
}

private JToken ResolveVariablePath(string path, JObject context)
{
    var parts = path.Split('.');
    JToken current = context;
    
    foreach (var part in parts)
    {
        if (current is JObject obj && obj.TryGetValue(part, out var next))
            current = next;
        else
            return JValue.CreateNull();
    }
    return current;
}
```

### Self-Learning Pattern Storage

After successful multi-step workflows, patterns are logged to Dataverse:

```csharp
private async Task LogLearnedPatternAsync(string patternType, JArray steps, JArray results)
{
    // Build pattern summary
    var toolSequence = steps.Select(s => (s as JObject)?["tool"]?.ToString() ?? "unknown");
    var patternLine = $"- [{DateTime.UtcNow:yyyy-MM-dd HH:mm}] {patternType}: " +
                      $"{string.Join(" → ", toolSequence)}";
    
    // Get current patterns
    var url = BuildDataverseUrl($"tst_agentinstructionses({_cachedInstructionsRecordId})");
    var current = await SendDataverseRequest(HttpMethod.Get, url, null);
    
    var existingPatterns = current["tst_learnedpatterns"]?.ToString() ?? "";
    var lines = existingPatterns.Split('\n').ToList();
    lines.Add(patternLine);
    
    // Limit to last 50 patterns to prevent bloat
    if (lines.Count > 50) lines = lines.Skip(lines.Count - 50).ToList();
    
    // Update record
    await SendDataverseRequest(new HttpMethod("PATCH"), url, new JObject
    {
        ["tst_learnedpatterns"] = string.Join("\n", lines),
        ["tst_updatecount"] = (current["tst_updatecount"]?.Value<int?>() ?? 0) + 1,
        ["tst_lastupdated"] = DateTime.UtcNow.ToString("o")
    });
}
```

### MCP Protocol Compliance

```yaml
/mcp:
  post:
    operationId: McpEndpoint
    x-ms-agentic-protocol: mcp-streamable-1.0
```

## Performance Gains

| Metric | Static MCP | Orchestrated MCP |
|--------|-----------|------------------|
| **Tool definitions in context** | 45 tools × ~200 tokens = **9,000 tokens** | 4 orchestration tools = **800 tokens** |
| **Relevant tools surfaced** | All 45 (model picks) | Top 3-5 by relevance |
| **Multi-step workflows** | N round-trips | 1 round-trip via `execute_workflow` |
| **Pattern learning** | None | Accumulated in Dataverse |
| **Tool updates** | Redeploy connector | Update Dataverse record |

For a typical "create account + contact + opportunity" flow:
- **Static**: 3 tool calls = 3 round-trips, ~15 seconds
- **Orchestrated**: 1 `execute_workflow` call = 1 round-trip, ~5 seconds

## Setup and Configuration

### Prerequisites

1. **Power Platform environment** with Dataverse
2. **Create `tst_agentinstructions` table** (see readme for schema)
3. **Populate initial agents.md** content with tool definitions

### Deployment

1. Import connector via Power Platform maker portal
2. Enable custom code, paste `script.csx`
3. Create table record with `tst_name = 'dataverse-tools-agent'`
4. Configure OAuth connection to Dataverse

### Copilot Studio Integration

1. Add connector as action in your agent
2. The `/mcp` endpoint enables tool discovery
3. Test with: "Search for tools to create accounts"

## Real-World Use Cases

### IT Service Desk
Agents search for ticket management tools, execute workflows to create/assign/resolve incidents, and learn resolution patterns from history.

### Sales Operations
Discovery of opportunity tools, bulk updates to pipeline stages, pattern-based recommendations for deal progression.

### HR Onboarding
Workflow orchestration for employee setup: create user → assign team → set security role → initialize from template.

### Compliance Auditing
Search audit and security tools, check access patterns across records, surface permission anomalies.

## Try It Yourself

The complete connector is available in my [SharingIsCaring](https://github.com/troystaylor/SharingIsCaring) repository:

- `apiDefinition.swagger.json` - OpenAPI 2.0 with MCP marker
- `apiProperties.json` - Connector metadata
- `script.csx` - C# implementation (~2700 lines)
- `agents.md` - Tool definitions with categories and keywords
- `readme.md` - Full documentation

## Additional Resources

### Anthropic Engineering
- [Building Effective Agents](https://www.anthropic.com/engineering/building-effective-agents) - Agent patterns and tool design philosophy
- [Claude Code Best Practices](https://www.anthropic.com/engineering/claude-code-best-practices) - CLAUDE.md pattern for dynamic context

### Microsoft Documentation
- [Dataverse Web API](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/overview)
- [Power Platform Custom Connectors](https://learn.microsoft.com/connectors/custom-connectors/)
- [Copilot Studio Actions](https://learn.microsoft.com/microsoft-copilot-studio/advanced-connectors)
- [Model Context Protocol](https://modelcontextprotocol.io/)

---

The orchestration pattern represents a shift from "give the AI all the tools" to "teach the AI to find and compose tools." By adding discovery, dynamic execution, and organizational learning, we're building agents that get smarter over time—not just at using Dataverse, but at understanding *how your organization* uses Dataverse.

What orchestration patterns would you add? Let me know on [LinkedIn](https://www.linkedin.com/in/introtroytaylor/) or [GitHub](https://github.com/troystaylor)!
