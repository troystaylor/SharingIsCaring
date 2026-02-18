# Power MCP Template v2 — Copilot Instructions

## What This Is

A Power Platform custom connector template implementing the Model Context Protocol (MCP) 2025-11-25 specification. The `script.csx` file contains two clearly marked sections:

1. **Section 1: MCP Framework** (~lines 1–430) — The `McpRequestHandler`, `McpSchemaBuilder`, content helpers, and all supporting types. **Do not modify** unless extending the framework itself.
2. **Section 2: Connector Entry Point** (~lines 430+) — Server config, `RegisterTools()` with tool definitions, `ExecuteAsync()`, helpers, and Application Insights logging. **This is where all user work happens.**

## Critical Constraints

### Power Platform Sandbox

This code runs in the Power Platform custom connector sandbox — a stateless, restricted .NET runtime. Every edit must respect these rules:

- **Allowed namespaces only**: `System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Net`, `System.Net.Http`, `System.Net.Http.Headers`, `System.Text`, `System.Text.RegularExpressions`, `System.Threading`, `System.Threading.Tasks`, `System.Web`, `System.Xml`, `System.Xml.Linq`, `System.Security.Cryptography`, `Newtonsoft.Json`, `Newtonsoft.Json.Linq`, `Microsoft.Extensions.Logging`
- **Forbidden namespaces**: `System.Reflection`, `System.ComponentModel`, `System.Data`, `System.Diagnostics.Process`, `System.IO.File` (no filesystem access)
- **No DI container**: No `IServiceCollection`, no constructor injection, no `IServiceProvider`
- **No ASP.NET Core**: No `WebApplication`, no middleware, no `IHost`
- **Stateless execution**: Each request creates a fresh `Script` instance — no state persists between requests
- **Single file**: All code lives in `script.csx` — no project files, no NuGet references

### Ambiguous Type References

The Power Platform runtime includes both `Newtonsoft.Json` and `System.Xml` namespaces. Always fully qualify:

```csharp
// WRONG — causes CS0104
body.ToString(Formatting.None)

// CORRECT
body.ToString(Newtonsoft.Json.Formatting.None)
```

### MCP Protocol Rules

- **JSON-RPC 2.0**: Every response must include `jsonrpc`, `id`, and either `result` or `error`
- **Copilot Studio requires responses for ALL methods**, including notifications (which the spec says should have no response). Always return `SerializeSuccess(id, new JObject())` for notifications.
- **Tool errors are tool results, not protocol errors**: Exceptions in tool handlers are returned as `{ "content": [...], "isError": true }`, not as JSON-RPC error responses. This allows the AI model to self-correct.
- **Input validation errors** (wrong type, missing required field) should throw `ArgumentException` — they get wrapped as tool errors automatically.
- **Protocol errors** (unknown method, parse failure) use `McpErrorCode` enum values.

## How to Add a Tool

All tool work happens in `RegisterTools()`. Add a single `handler.AddTool()` call:

```csharp
handler.AddTool("tool_name", "What this tool does — be descriptive for AI.",
    schema: s => s
        .String("param1", "Description", required: true)
        .Integer("top", "Max results", defaultValue: 10),
    handler: async (args, ct) =>
    {
        var param1 = RequireArgument(args, "param1");
        var top = args.Value<int?>("top") ?? 10;
        // Tool logic here
        return new JObject { ["result"] = "value" };
    },
    annotations: a => { a["readOnlyHint"] = true; });
```

### AddTool Parameters

| Parameter | Required | Purpose |
|---|---|---|
| `name` | Yes | Tool identifier (snake_case) |
| `description` | Yes | AI-readable description of what the tool does |
| `schema` | Yes | `McpSchemaBuilder` lambda for `inputSchema` |
| `handler` | Yes | `Func<JObject, CancellationToken, Task<JObject>>` — the tool logic |
| `annotations` | No | MCP annotations: `readOnlyHint`, `idempotentHint`, `openWorldHint`, etc. |
| `title` | No | Human-readable display name (new in 2025-11-25) |
| `outputSchemaConfig` | No | `McpSchemaBuilder` lambda for `outputSchema` (enables `structuredContent`) |

### McpSchemaBuilder Methods

```csharp
s.String("name", "desc", required: true, format: "date-time", enumValues: new[] { "a", "b" })
s.Integer("count", "desc", defaultValue: 10)
s.Number("price", "desc", required: true)
s.Boolean("active", "desc")
s.Array("items", "desc", itemSchema: new JObject { ["type"] = "string" })
s.Object("address", "desc", nested => nested.String("city", "City").String("zip", "ZIP"))
```

## Rich Content Types

By default, handler return values are auto-wrapped as `text` content. For image, audio, or resource content, use the static helpers:

```csharp
// Return mixed content
return McpRequestHandler.ToolResult(new JArray
{
    McpRequestHandler.TextContent("Description text"),
    McpRequestHandler.ImageContent(base64Data, "image/png")
});

// With structured content (requires outputSchemaConfig on the tool)
return McpRequestHandler.ToolResult(
    content: new JArray { McpRequestHandler.TextContent("65°F") },
    structuredContent: new JObject { ["temp"] = 65 });
```

## Helper Methods Available

| Method | Purpose |
|---|---|
| `RequireArgument(args, "name")` | Get required string argument; throws `ArgumentException` if missing |
| `GetArgument(args, "name", "default")` | Get optional string argument with fallback |
| `GetConnectionParameter("name")` | Read a connector connection parameter (null-safe) |
| `SendExternalRequestAsync(method, url, body)` | Forward-auth HTTP request to external API |

## What NOT to Change

- **Section 1** classes: `McpRequestHandler`, `McpSchemaBuilder`, `McpToolDefinition`, `McpServerInfo`, `McpCapabilities`, `McpServerOptions`, `McpErrorCode`, `McpException`
- **HandleAsync switch statement**: Protocol method routing is complete for the spec
- **JSON-RPC serialization methods**: `SerializeSuccess`, `SerializeError`
- **Content helpers**: `TextContent`, `ImageContent`, `AudioContent`, `ResourceContent`, `ToolResult`

## What IS Safe to Change

- `McpServerOptions` values (server name, version, title, capabilities)
- `APP_INSIGHTS_CONNECTION_STRING` constant
- `Instructions` text
- `RegisterTools()` contents — add/remove/modify tool registrations
- Add new private helper methods in the `Script` class
- `ExecuteAsync()` if you need custom pre/post processing

## Stateless Limitations

Power Platform connectors cannot maintain state between requests or send asynchronous messages. These MCP 2025-11-25 features are **not implementable**:

- **Tasks** (experimental) — requires persistent state for polling/deferred results
- **Server→client requests** — `sampling/createMessage`, `elicitation/create`, `roots/list`
- **Server→client notifications** — `notifications/progress`, `notifications/message`, `notifications/tools/list_changed`, `notifications/resources/list_changed`, etc.

Do not attempt to implement these features. They are documented as no-ops or excluded from the handler.
