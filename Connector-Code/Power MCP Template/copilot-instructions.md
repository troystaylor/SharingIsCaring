# Power MCP Template — Copilot Instructions

## What This Is

A Power Platform custom connector template implementing the Model Context Protocol **2026-07-28** specification, serving both protocol eras from one endpoint. The `script.csx` file has two clearly marked sections:

1. **Section 1: Connector Entry Point** — Server config, `RegisterCapabilities()` with tool/resource/prompt/skill definitions, `ExecuteAsync()`, helpers, and Application Insights logging. **This is where all user work happens.**
2. **Section 2: MCP Framework** — `McpRequestHandler`, `McpSchemaBuilder`, content helpers, and all supporting types. **Do not modify** unless extending the framework itself.

## Critical Constraints

### Dual-era serving — do not remove the legacy path

`2026-07-28` deleted the `initialize` handshake and made MCP stateless. Copilot Studio has not moved yet: it still opens with `initialize` and sends no `_meta`. The framework picks an era per request and **must keep answering both**.

- Modern iff `params._meta["io.modelcontextprotocol/protocolVersion"]` is present, or the `MCP-Protocol-Version` header is set, or the method is `server/discover`.
- Modern-only fields — `resultType`, `ttlMs`, `cacheScope`, `_meta.serverInfo` — must **never** appear in a legacy response. Adding them unconditionally is the single most likely way to break a live Copilot Studio agent.
- All handlers take an `McpRequestContext`. Never reintroduce a bare `JToken id` parameter.

### Power Platform Sandbox

This code runs in the Power Platform custom connector sandbox — a stateless, restricted .NET runtime. Every edit must respect these rules:

- **Allowed namespaces only**: `System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Net`, `System.Net.Http`, `System.Net.Http.Headers`, `System.Text`, `System.Text.RegularExpressions`, `System.Threading`, `System.Threading.Tasks`, `System.Web`, `System.Xml`, `System.Xml.Linq`, `System.Security.Cryptography`, `Newtonsoft.Json`, `Newtonsoft.Json.Linq`, `Microsoft.Extensions.Logging`
- **Forbidden namespaces**: `System.Reflection`, `System.ComponentModel`, `System.Data`, `System.Diagnostics.Process`, `System.IO.File` (no filesystem access)
- **No DI container**: No `IServiceCollection`, no constructor injection, no `IServiceProvider`
- **No ASP.NET Core**: No `WebApplication`, no middleware, no `IHost`
- **Stateless execution**: Each request creates a fresh `Script` instance — no state persists between requests
- **Single file**: All code lives in `script.csx` — no project files, no NuGet references
- **No `new HttpClient()`**: use `this.Context.SendAsync(request, this.CancellationToken)`

### Ambiguous Type References

The Power Platform runtime includes both `Newtonsoft.Json` and `System.Xml` namespaces. Always fully qualify:

```csharp
// WRONG — causes CS0104
body.ToString(Formatting.None)

// CORRECT
body.ToString(Newtonsoft.Json.Formatting.None)
```

### Schema rules — narrower than the spec on purpose

`2026-07-28` permits any JSON Schema 2020-12 keyword. **Do not use that latitude.** Copilot Studio has four documented schema defects, and the worst is silent:

| Never emit | Because |
|---|---|
| `$ref`, `$defs`, any reference type | Copilot Studio **drops the tool from `tools/list` with no error** |
| `"type": ["string", "null"]` | Truncates the schema |
| numeric `exclusiveMinimum` | Throws `System.FormatException`; use the draft-04 boolean form |

Inline nested objects instead. `McpSchemaBuilder.Object()` already does this — keep it that way.

### MCP Protocol Rules

- **JSON-RPC 2.0**: Every response includes `jsonrpc`, `id`, and either `result` or `error`
- **Answer every method, including notifications.** The spec says notifications get no response, but a connector is an HTTP request/response function and Copilot Studio requires a body. Return `SerializeSuccess(ctx, new JObject())`.
- **Tool errors are tool results, not protocol errors**: exceptions in tool handlers become `{ "content": [...], "isError": true }` so the model can self-correct
- **Input validation errors** should throw `ArgumentException` — they get wrapped as tool errors automatically
- **Protocol errors** use `McpErrorCode`. New codes must come from the `-32020` to `-32099` range; `-32000` to `-32019` is frozen legacy.
- **Removed in 2026-07-28**: `initialize`, `notifications/initialized`, `notifications/roots/list_changed`, `ping`, `logging/setLevel`, `resources/subscribe`, `resources/unsubscribe`. Modern callers get `MethodNotFound`; legacy callers still get the old behavior.

## How to Add a Tool

All tool work happens in `RegisterCapabilities()`. Add a single `handler.AddTool()` call:

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
| `title` | No | Human-readable display name |
| `outputSchemaConfig` | No | `McpSchemaBuilder` lambda for `outputSchema` (enables `structuredContent`) |

The parameter names are `schema` and `annotations`, **not** `schemaConfig` / `annotationsConfig`. An earlier revision had that mismatch and the template would not compile.

### McpSchemaBuilder Methods

```csharp
s.String("name", "desc", required: true, format: "date-time", enumValues: new[] { "a", "b" })
s.Integer("count", "desc", defaultValue: 10)
s.Number("price", "desc", required: true)
s.Boolean("active", "desc")
s.Array("items", "desc", itemSchema: new JObject { ["type"] = "string" })
s.Object("address", "desc", nested => nested.String("city", "City").String("zip", "ZIP"))
```

A tool with no parameters yields `{ "type": "object", "additionalProperties": false }` automatically.

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
| `McpRequestHandler.ExtractUriParameters(template, uri)` | Extract `{param}` values from a URI given a template |

## How to Add a Resource

Register resources in `RegisterCapabilities()`. Static resources use `AddResource`, dynamic ones use `AddResourceTemplate`:

```csharp
// Static resource (fixed URI)
handler.AddResource("data://config/info", "Config Info", "Server configuration.",
    handler: async (ct) => new JArray
    {
        new JObject { ["uri"] = "data://config/info", ["mimeType"] = "application/json",
            ["text"] = new JObject { ["version"] = "1.0" }.ToString(Newtonsoft.Json.Formatting.Indented) }
    });

// Dynamic resource template
handler.AddResourceTemplate("data://records/{id}", "Record by ID", "Fetch a record.",
    handler: async (uri, ct) =>
    {
        var p = McpRequestHandler.ExtractUriParameters("data://records/{id}", uri);
        return new JArray { new JObject { ["uri"] = uri, ["mimeType"] = "application/json",
            ["text"] = new JObject { ["id"] = p["id"] }.ToString(Newtonsoft.Json.Formatting.Indented) } };
    });
```

## How to Add a Prompt

Register prompts in `RegisterCapabilities()`. The handler returns a `JArray` of message objects:

```csharp
handler.AddPrompt("summarize", "Summarize the given text.",
    arguments: new List<McpPromptArgument>
    {
        new McpPromptArgument { Name = "text", Description = "Text to summarize", Required = true }
    },
    handler: async (args, ct) =>
    {
        var text = args.Value<string>("text") ?? "";
        return new JArray
        {
            new JObject
            {
                ["role"] = "user",
                ["content"] = new JObject { ["type"] = "text", ["text"] = $"Summarize: {text}" }
            }
        };
    });
```

## How to Add a Skill

`AddSkill()` publishes an Agent Skill over MCP. It is pure `AddResource` sugar — it registers `skill://index.json`, `skill://<name>/SKILL.md`, and one resource per sibling file. No protocol change.

```csharp
handler.AddSkill("expense-report",
    "File and validate employee expense reports. Use when asked about expense submissions or spending limits.",
    instructions: "1. Read the policy-limits resource.\n2. Validate each line item against the caps.",
    resources: r => r
        .Resource("references/policy-limits.md", "Per-category spending caps.", () => limitsMarkdown),
    license: "Apache-2.0",
    compatibility: "Requires network access to the Contoso Finance API.");
```

Constraints enforced at registration (they throw if violated): `name` is lowercase letters, digits, and single hyphens, max 64 characters, no leading/trailing/consecutive hyphen; `description` max 1024; `compatibility` max 500. Only the `skill-md` distribution type is generated.

## Multi Round-Trip Requests

MRTR replaced server-initiated elicitation. Return `McpRequestHandler.InputRequired(...)` from a tool handler:

```csharp
return McpRequestHandler.InputRequired(
    new JObject
    {
        ["confirm"] = McpRequestHandler.ElicitationRequest(
            "Delete all matching records?",
            s => s.Boolean("proceed", "Confirm deletion", required: true))
    },
    requestState: filter);
```

The client retries the same `tools/call` with `inputResponses` and `requestState`. **The server keeps nothing between the two calls** — everything needed to resume must be in `requestState`, and it must not contain secrets since it round-trips through the caller.

Legacy clients cannot read `resultType`, so the framework degrades an `input_required` into a tool error naming the missing input. Do not remove that fallback.

## What NOT to Change

- **Section 2** types: `McpRequestHandler`, `McpRequestContext`, `McpTransportHeaders`, `McpSchemaBuilder`, `McpKeys`, `McpCacheKind`, `McpToolDefinition`, `McpResourceDefinition`, `McpResourceTemplateDefinition`, `McpPromptDefinition`, `McpPromptArgument`, `McpSkillDefinition`, `McpSkillResourceBuilder`, `McpServerInfo`, `McpCapabilities`, `McpServerOptions`, `McpErrorCode`, `McpException`
- **Era detection**: `BuildContext`, `IsSupportedVersion`, `ValidateHeaders`
- **HandleAsync switch statement**: routing is complete for both eras
- **JSON-RPC serialization**: `SerializeSuccess`, `SerializeError`, `ApplyCacheHints`, `BuildServerInfo`, `BuildCapabilities`
- **Content helpers**: `TextContent`, `ImageContent`, `AudioContent`, `ResourceContent`, `ResourceLinkContent`, `ToolResult`, `InputRequired`, `ElicitationRequest`
- **URI template helpers**: `MatchesUriTemplate`, `ExtractUriParameters`
- **Skill index generation**: `BuildSkillIndexContents`, `McpSkillDefinition.RenderSkillMarkdown`

## What IS Safe to Change

- `McpServerOptions` values — server identity, capabilities, `SupportedProtocolVersions`, cache TTLs and scopes, `ValidateRequestHeaders`
- `APP_INSIGHTS_CONNECTION_STRING` constant
- `Instructions` text (it is surfaced by `server/discover`)
- `RegisterCapabilities()` contents — add, remove, or modify tool, resource, prompt, and skill registrations
- New private helper methods on the `Script` class
- `ExecuteAsync()` if you need custom pre/post processing, as long as it keeps passing `McpTransportHeaders.FromRequest(...)`

## Still Not Implementable

Only two, and both need a stream the connector cannot hold open:

- `subscriptions/listen` and every server-to-client notification
- the `io.modelcontextprotocol/tasks` extension, which needs durable state

Sessions and elicitation are **no longer** on this list. The protocol dropped sessions entirely, and MRTR replaced streamed elicitation with a retry.

## Validating a Change

There is no compiler in the Power Platform authoring experience, and the sandbox will happily accept a file that cannot build until runtime. Before shipping an edit to `script.csx`:

1. Copy it into a throwaway .NET console project alongside a `ScriptBase` / `IScriptContext` stub, with `<Nullable>disable</Nullable>` and the Newtonsoft.Json plus Microsoft.Extensions.Logging.Abstractions packages, and build it.
2. Run `ppcv` against the connector folder to check the swagger, properties, and script together.
3. Exercise both eras: one request with `_meta["io.modelcontextprotocol/protocolVersion"]` and one without, confirming the legacy response contains no `resultType`, `ttlMs`, `cacheScope`, or `_meta`.

Do not attempt to implement these features. They are documented as no-ops or excluded from the handler.
