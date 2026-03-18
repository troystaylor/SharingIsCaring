# Power MCP Template v2.1

The next generation of the [Power MCP Template](../Power%20MCP%20Template/) — write tools, resources, and prompts, not protocol code.

v1 gave you a working MCP server with full protocol handling visible in your script. v2 moved all of that into a built-in `McpRequestHandler` framework with a fluent `AddTool` API. v2.1 completes the picture with `AddResource`, `AddResourceTemplate`, and `AddPrompt` — all three advertised MCP capabilities now have first-class registration APIs.

**Spec coverage: MCP 2025-11-25** — handles all server-side protocol methods including tools, resources, prompts, completions, logging, resource subscriptions, and all client notifications.

## What Changed from v1

The official MCP C# SDK uses ASP.NET Core hosting (`builder.Services.AddMcpServer()` / `app.MapMcp()`). Power Platform custom connectors use `ScriptBase.ExecuteAsync()` — a stateless request-in/response-out function with no DI container, and a [restricted set of namespaces](https://learn.microsoft.com/en-us/connectors/custom-connectors/write-code#supported-namespaces) (notably excluding `System.Reflection` and `System.ComponentModel`). v2 includes a built-in `McpRequestHandler` that bridges this gap, using fluent `AddTool`, `AddResource`, `AddResourceTemplate`, and `AddPrompt` APIs that work within the sandbox.

## What the Framework Handles

| v1 (Manual Code) | v2 (Handled by Framework) |
|---|---|
| JSON-RPC response helpers (~40 lines) | Built into `McpRequestHandler` |
| Method routing switch (~60 lines) | Built into `McpRequestHandler` |
| `HandleInitialize` (~30 lines) | Driven from `McpServerOptions` |
| `BuildToolsList` / `HandleToolsList` (~60 lines) | Auto-built from `AddTool` registrations |
| `HandleToolsCallAsync` / `ExecuteToolAsync` (~70 lines) | Auto-dispatched to registered handlers |
| Resource list/read stubs (~10 lines) | Auto-built from `AddResource` / `AddResourceTemplate` |
| Prompt list/get stubs (~10 lines) | Auto-built from `AddPrompt` registrations |
| Notification handlers (~10 lines) | Handled automatically |
| Empty capability stubs (~20 lines) | Declared via `McpCapabilities` |
| `RequireArgument` / `GetArgument` helpers (~20 lines) | Direct `args.Value<T>()` access |
| Manual JSON Schema for each tool (~50 lines) | Built with `McpSchemaBuilder` fluent API |

**Net result: ~500 lines → ~60 lines of user code.** You only write tool, resource, and prompt definitions.

## Quick Start

### 1. Define Your Capabilities

In the `RegisterCapabilities` method inside the `Script` class, add tools, resources, and prompts:

```csharp
private void RegisterCapabilities(McpRequestHandler handler)
{
    handler.AddTool("echo", "Echoes back the provided message. Useful for testing connectivity.",
        schema: s => s.String("message", "The message to echo back", required: true),
        handler: async (args, ct) =>
        {
            return new JObject
            {
                ["echo"] = args.Value<string>("message") ?? "No message provided",
                ["timestamp"] = DateTime.UtcNow.ToString("o")
            };
        },
        annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

    handler.AddResource("data://config/info", "Server Info", "Server configuration.",
        handler: async (ct) => new JArray
        {
            new JObject { ["uri"] = "data://config/info", ["mimeType"] = "application/json",
                ["text"] = new JObject { ["version"] = "1.0" }.ToString(Newtonsoft.Json.Formatting.Indented) }
        });

    handler.AddPrompt("summarize", "Summarize the given text.",
        arguments: new List<McpPromptArgument>
        {
            new McpPromptArgument { Name = "text", Description = "Text to summarize", Required = true }
        },
        handler: async (args, ct) => new JArray
        {
            new JObject { ["role"] = "user",
                ["content"] = new JObject { ["type"] = "text", ["text"] = $"Summarize: {args.Value<string>("text")}" } }
        });
}
```

### 2. Wire Up in ExecuteAsync

The entry point is already set up in the template — you just add capabilities to `RegisterCapabilities`:

```csharp
public override async Task<HttpResponseMessage> ExecuteAsync()
{
    var handler = new McpRequestHandler(Options);
    RegisterCapabilities(handler);

    var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
    var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

    return new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(result, Encoding.UTF8, "application/json")
    };
}
```

That's it. The handler does the rest.

## Tool Registration

Register tools using `handler.AddTool()` inside the `RegisterCapabilities` method. Each tool needs a name, description, schema, and handler function:

```csharp
handler.AddTool("search_records", "Search records by keyword.",
    schema: s => s
        .String("query", "Search keyword", required: true)
        .Integer("top", "Max results to return", defaultValue: 10),
    handler: async (args, ct) =>
    {
        var query = args.Value<string>("query");
        var top = args.Value<int?>("top") ?? 10;
        // Your search logic here
        return new JObject { ["results"] = new JArray() };
    },
    annotations: a => { a["readOnlyHint"] = true; });
```

### Tool Title and Output Schema

Tools can declare a `title` for human-readable display and an `outputSchema` for structured content validation:

```csharp
handler.AddTool("get_weather", "Get current weather for a city.",
    schema: s => s.String("city", "City name", required: true),
    handler: async (args, ct) =>
    {
        return new JObject { ["city"] = args.Value<string>("city"), ["temp"] = 72 };
    },
    title: "Get Weather",
    outputSchemaConfig: o => o
        .String("city", "City name")
        .Number("temp", "Temperature in Fahrenheit"));
```

**What the framework does for you:**
- `inputSchema` is built from the `McpSchemaBuilder` calls
- Parameters marked `required: true` are added to the JSON Schema `required` array
- `defaultValue` on integer params sets the `default` in the schema
- Annotations map directly to [MCP tool annotations](https://modelcontextprotocol.io/specification/2025-11-25/server/tools#annotations) (`readOnlyHint`, `idempotentHint`, `openWorldHint`, etc.)
- Tool calls are dispatched by name to the matching handler
- Return values are wrapped in the MCP content response format automatically

### Schema Builder API

The `McpSchemaBuilder` supports all JSON Schema types:

```csharp
s.String("name", "description", required: true, format: "date", enumValues: new[] { "a", "b" })
s.Integer("count", "description", required: false, defaultValue: 10)
s.Number("price", "description", required: true)
s.Boolean("active", "description")
s.Array("items", "description", itemSchema: new JObject { ["type"] = "string" }, required: true)
s.Object("address", "Mailing address", nested => nested
    .String("street", "Street line", required: true)
    .String("city", "City", required: true)
    .String("zip", "ZIP code"), required: true)
```

## External API Calls

Tools that need to call external APIs can capture `ScriptBase` context via closures. Since `RegisterCapabilities` is an instance method on the `Script` class, you have access to `this.Context` and `this.CancellationToken`:

```csharp
private void RegisterCapabilities(McpRequestHandler handler)
{
    handler.AddTool("get_customer", "Retrieve a customer by ID.",
        schema: s => s.String("id", "Customer ID", required: true),
        handler: async (args, ct) =>
        {
            var id = args.Value<string>("id");
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.example.com/customers/{id}");

            // Forward OAuth token from connector
            if (this.Context.Request.Headers.Authorization != null)
                request.Headers.Authorization = this.Context.Request.Headers.Authorization;

            var response = await this.Context.SendAsync(request, ct).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JObject.Parse(content);
        },
        annotations: a => { a["readOnlyHint"] = true; });
}
```

## Resource Registration

Resources expose data that MCP clients can read without invoking a tool. Register resources using `handler.AddResource()` for static (fixed URI) resources or `handler.AddResourceTemplate()` for dynamic (parameterized URI) resources.

### Static Resources

Static resources have a fixed URI and are listed directly in `resources/list`:

```csharp
handler.AddResource("data://config/server-info", "Server Info",
    "Current server configuration and version.",
    handler: async (ct) =>
    {
        return new JArray
        {
            new JObject
            {
                ["uri"] = "data://config/server-info",
                ["mimeType"] = "application/json",
                ["text"] = new JObject
                {
                    ["name"] = "my-server",
                    ["version"] = "1.0.0"
                }.ToString(Newtonsoft.Json.Formatting.Indented)
            }
        };
    });
```

### Resource Templates

Resource templates use [RFC 6570](https://tools.ietf.org/html/rfc6570) style URI templates with `{param}` placeholders. They're listed in `resources/templates/list` and resolved at read time:

```csharp
handler.AddResourceTemplate("data://customers/{id}", "Customer by ID",
    "Retrieve a customer record by ID.",
    handler: async (uri, ct) =>
    {
        var p = McpRequestHandler.ExtractUriParameters("data://customers/{id}", uri);
        var id = p["id"];

        // Fetch from API, database, etc.
        return new JArray
        {
            new JObject
            {
                ["uri"] = uri,
                ["mimeType"] = "application/json",
                ["text"] = new JObject { ["id"] = id, ["name"] = "Contoso" }
                    .ToString(Newtonsoft.Json.Formatting.Indented)
            }
        };
    });
```

### Resource Content Format

Resource handlers return a `JArray` of content objects. Each object has:

| Field | Required | Description |
|---|---|---|
| `uri` | Yes | The resource URI |
| `mimeType` | Yes | MIME type of the content |
| `text` | One of these | Text content (for text-based resources) |
| `blob` | One of these | Base64-encoded binary content |

### AddResource Parameters

| Parameter | Required | Default | Description |
|---|---|---|---|
| `uri` | Yes | — | Fixed resource URI |
| `name` | Yes | — | Human-readable name for the resource |
| `description` | Yes | — | Description of the resource |
| `handler` | Yes | — | `Func<CancellationToken, Task<JArray>>` returning content |
| `mimeType` | No | `application/json` | MIME type of the resource content |
| `annotationsConfig` | No | null | Optional audience/priority annotations |

### AddResourceTemplate Parameters

| Parameter | Required | Default | Description |
|---|---|---|---|
| `uriTemplate` | Yes | — | URI template with `{param}` placeholders |
| `name` | Yes | — | Human-readable name for the template |
| `description` | Yes | — | Description of the template |
| `handler` | Yes | — | `Func<string, CancellationToken, Task<JArray>>` receiving the resolved URI |
| `mimeType` | No | `application/json` | MIME type of the resource content |
| `annotationsConfig` | No | null | Optional audience/priority annotations |

### ExtractUriParameters Helper

Use `McpRequestHandler.ExtractUriParameters()` inside resource template handlers to extract named parameters:

```csharp
// Template: "data://items/{category}/{id}"
// URI:      "data://items/electronics/42"
var p = McpRequestHandler.ExtractUriParameters("data://items/{category}/{id}", uri);
// p["category"] = "electronics"
// p["id"] = "42"
```

## Prompt Registration

Prompts expose reusable message templates that MCP clients can retrieve via `prompts/list` and `prompts/get`. Register prompts using `handler.AddPrompt()` inside `RegisterCapabilities`.

### Basic Prompt

```csharp
handler.AddPrompt("summarize", "Summarize the given text concisely.",
    arguments: new List<McpPromptArgument>
    {
        new McpPromptArgument { Name = "text", Description = "The text to summarize", Required = true },
        new McpPromptArgument { Name = "style", Description = "Summary style: brief, detailed, or bullets", Required = false }
    },
    handler: async (args, ct) =>
    {
        var text = args.Value<string>("text") ?? "";
        var style = args.Value<string>("style") ?? "brief";

        return new JArray
        {
            new JObject
            {
                ["role"] = "user",
                ["content"] = new JObject
                {
                    ["type"] = "text",
                    ["text"] = $"Please provide a {style} summary of the following text:\n\n{text}"
                }
            }
        };
    });
```

### Multi-Turn Prompt

Prompts can return multiple messages to set up a conversation:

```csharp
handler.AddPrompt("code_review", "Review code for issues and improvements.",
    arguments: new List<McpPromptArgument>
    {
        new McpPromptArgument { Name = "code", Description = "The code to review", Required = true },
        new McpPromptArgument { Name = "language", Description = "Programming language", Required = false }
    },
    handler: async (args, ct) =>
    {
        var code = args.Value<string>("code") ?? "";
        var language = args.Value<string>("language") ?? "unknown";

        return new JArray
        {
            new JObject
            {
                ["role"] = "user",
                ["content"] = new JObject { ["type"] = "text",
                    ["text"] = $"Please review the following {language} code for bugs, security issues, and improvements:\n\n```{language}\n{code}\n```" }
            },
            new JObject
            {
                ["role"] = "assistant",
                ["content"] = new JObject { ["type"] = "text",
                    ["text"] = "I'll review this code for bugs, security issues, and potential improvements. Let me analyze it section by section." }
            }
        };
    });
```

### Prompt Message Format

Prompt handlers return a `JArray` of message objects. Each message has:

| Field | Required | Values |
|---|---|---|
| `role` | Yes | `"user"` or `"assistant"` |
| `content` | Yes | Object with `type` and `text` fields |

### AddPrompt Parameters

| Parameter | Required | Description |
|---|---|---|
| `name` | Yes | Prompt identifier (snake_case) |
| `description` | Yes | AI-readable description of the prompt's purpose |
| `arguments` | Yes | `List<McpPromptArgument>` describing accepted parameters |
| `handler` | Yes | `Func<JObject, CancellationToken, Task<JArray>>` returning messages |

### McpPromptArgument Properties

| Property | Type | Description |
|---|---|---|
| `Name` | string | Argument name |
| `Description` | string | Human-readable description |
| `Required` | bool | Whether the argument must be provided |

## Rich Content Types

By default, tool handlers return a `JObject` which is automatically wrapped in a `text` content item. To return image, audio, resource, or mixed content, use the static content helpers and `McpRequestHandler.ToolResult()`:

```csharp
handler.AddTool("get_chart", "Generate a chart image.",
    schema: s => s.String("metric", "Metric to chart", required: true),
    handler: async (args, ct) =>
    {
        var imageBytes = await GenerateChartAsync(args.Value<string>("metric"));
        var base64 = Convert.ToBase64String(imageBytes);

        return McpRequestHandler.ToolResult(new JArray
        {
            McpRequestHandler.TextContent("Here is the chart:"),
            McpRequestHandler.ImageContent(base64, "image/png")
        });
    });
```

### Content Helpers

| Helper | Content Type | Parameters |
|---|---|---|
| `TextContent(text)` | `text` | Plain text string |
| `ImageContent(base64Data, mimeType)` | `image` | Base64-encoded image data |
| `AudioContent(base64Data, mimeType)` | `audio` | Base64-encoded audio data |
| `ResourceContent(uri, text, mimeType)` | `resource` | Embedded resource with URI |

### Structured Content

Return `structuredContent` alongside content for tools that declare an `outputSchema`:

```csharp
handler.AddTool("get_weather", "Get weather data.",
    schema: s => s.String("city", "City name", required: true),
    handler: async (args, ct) =>
    {
        var data = new JObject { ["city"] = "Seattle", ["temp"] = 65 };
        return McpRequestHandler.ToolResult(
            content: new JArray { McpRequestHandler.TextContent("65°F in Seattle") },
            structuredContent: data);
    },
    outputSchemaConfig: o => o
        .String("city", "City name")
        .Number("temp", "Temperature"));
```

## Error Handling

### From Tool Methods

```csharp
// Structured MCP error (logged, returned as tool error)
throw new McpException(McpErrorCode.InvalidParams, "'id' is required");

// Argument validation (returned as tool error)
throw new ArgumentException("ID must be a valid GUID");

// Any other exception (returned as tool error, logged)
throw new Exception("External API unavailable");
```

All exceptions from tools are returned as MCP tool results with `isError: true`, not as protocol-level errors. This lets the AI understand and react to failures.

### McpErrorCode Values

| Code | Name | Use |
|---|---|---|
| -32000 | RequestTimeout | Server timed out processing |
| -32700 | ParseError | Malformed JSON |
| -32600 | InvalidRequest | Missing required fields |
| -32601 | MethodNotFound | Unknown MCP method |
| -32602 | InvalidParams | Bad tool arguments |
| -32603 | InternalError | Unhandled exception |

## Application Insights (Optional)

Wire up telemetry via the `OnLog` callback:

```csharp
handler.OnLog = (eventName, data) =>
{
    this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
    _ = LogToAppInsights(eventName, data, correlationId);
};
```

The handler emits these events:

| Event | When |
|---|---|
| McpRequestReceived | Every incoming request |
| McpInitialized | After successful initialize |
| McpToolsListed | tools/list served |
| McpResourcesListed | resources/list served |
| McpResourceTemplatesListed | resources/templates/list served |
| McpResourceReadStarted | Before resource read |
| McpResourceReadCompleted | After successful resource read |
| McpResourceReadError | Resource read failed |
| McpPromptsListed | prompts/list served |
| McpPromptGetStarted | Before prompt get |
| McpPromptGetCompleted | After successful prompt get |
| McpPromptGetError | Prompt get failed |
| McpToolCallStarted | Before tool execution |
| McpToolCallCompleted | After successful tool execution |
| McpToolCallError | Tool execution failed |
| McpMethodNotFound | Unknown method received |
| McpError | Protocol-level error |

## File Structure

```
Power MCP Template v2/
├── script.csx      # Framework + entry point with tool definitions
└── readme.md       # This file
```

The script is organized into two clearly marked sections:

1. **Section 1: Connector Entry Point** — Server config, `RegisterCapabilities` with your tool/resource/prompt definitions, `ExecuteAsync` entry point, and optional Application Insights logging.
2. **Section 2: MCP Framework** — The `McpRequestHandler`, `McpSchemaBuilder`, and supporting types. Don't modify unless extending.

## Connector Setup

### apiDefinition.swagger.json

Use the standard MCP swagger contract (identical to v1):

```json
{
  "swagger": "2.0",
  "info": {
    "title": "MCP Server",
    "description": "MCP Server for Copilot Studio",
    "version": "1.0.0"
  },
  "host": "your-api-host.com",
  "basePath": "/mcp",
  "schemes": ["https"],
  "consumes": ["application/json"],
  "produces": ["application/json"],
  "paths": {
    "/": {
      "post": {
        "summary": "Invoke MCP Server",
        "x-ms-agentic-protocol": "mcp-streamable-1.0",
        "operationId": "InvokeMCP",
        "responses": {
          "200": { "description": "Success" }
        }
      }
    }
  },
  "securityDefinitions": {}
}
```

### apiProperties.json

```json
{
  "properties": {
    "connectionParameters": {},
    "policyTemplateInstances": [
      {
        "templateId": "routeRequestToCode",
        "title": "MCP Handler",
        "parameters": {
          "x-ms-apimTemplate-operationName": ["InvokeMCP"]
        }
      }
    ]
  }
}
```

## v1 vs v2.1

| Aspect | v1 (Power MCP Template) | v2.1 (This Template) |
|---|---|---|
| Lines of user code per tool | ~30 (schema + routing + handler) | ~8 (fluent `AddTool` call) |
| Adding a new tool | Edit 3 places | Add 1 `AddTool` call |
| Adding a resource | Not supported | Add 1 `AddResource` / `AddResourceTemplate` call |
| Adding a prompt | Not supported | Add 1 `AddPrompt` call |
| Schema definition | Manual JObject construction | `McpSchemaBuilder` fluent API |
| Protocol handling | Visible in your script | Hidden in framework |
| Error codes | Raw integers | `McpErrorCode` enum |
| Structured errors | Manual JObject wrapping | `throw new McpException(...)` |
| Sandbox compatibility | Always works | Always works (no reflection needed) |

## Relationship to Official MCP C# SDK

v2.1 mirrors the concepts from the [official SDK](https://github.com/modelcontextprotocol/csharp-sdk) adapted for Power Platform's stateless sandbox:

| Official SDK | v2.1 Equivalent |
|---|---|
| `McpServerOptions` | `McpServerOptions` (simplified for stateless use) |
| `IMcpServer` + DI | `McpRequestHandler` (stateless, no DI) |
| `McpException` | `McpException` (same pattern) |
| `builder.Services.AddMcpServer()` | `new McpRequestHandler(options)` |
| `app.MapMcp()` | `handler.HandleAsync(body, ct)` |
| `[McpServerTool]` + reflection | `handler.AddTool()` fluent API (no reflection) |
| `[McpServerResource]` + reflection | `handler.AddResource()` / `handler.AddResourceTemplate()` fluent API |
| `[McpServerPrompt]` + reflection | `handler.AddPrompt()` fluent API |

The official SDK uses attribute-based discovery with `System.Reflection`. Power Platform [does not support](https://learn.microsoft.com/en-us/connectors/custom-connectors/write-code#supported-namespaces) `System.Reflection` or `System.ComponentModel`, so v2.1 uses fluent registration APIs instead. If Microsoft enables these namespaces in the future, an attribute-based registration path could be added alongside the fluent APIs.

## MCP 2025-11-25 Spec Coverage

### Fully Handled

| Method | Notes |
|---|---|
| `initialize` | Server info, capabilities, protocol version negotiation |
| `initialized` / `notifications/initialized` | No-op acknowledgment |
| `notifications/cancelled` | No-op acknowledgment |
| `notifications/roots/list_changed` | No-op acknowledgment |
| `ping` | Empty result |
| `tools/list` | With `title`, `outputSchema`, `annotations` |
| `tools/call` | Text, image, audio, resource content + `structuredContent` |
| `resources/list` | Auto-built from `AddResource` registrations |
| `resources/templates/list` | Auto-built from `AddResourceTemplate` registrations |
| `resources/read` | Auto-dispatched to matching resource/template handler |
| `resources/subscribe` / `resources/unsubscribe` | No-op acknowledgment |
| `prompts/list` | Auto-built from `AddPrompt` registrations |
| `prompts/get` | Auto-dispatched to matching prompt handler |
| `completion/complete` | Empty completions |
| `logging/setLevel` | No-op acknowledgment |

### Stateless Limitations

Power Platform custom connectors are stateless request-in/response-out functions. They cannot maintain state between requests or send asynchronous notifications. The following MCP features require capabilities that are architecturally incompatible:

| Feature | Why Not Possible |
|---|---|
| **Tasks** (experimental) | Requires persistent state between requests for polling and deferred results |
| **Server→client requests** (`sampling/createMessage`, `elicitation/create`, `roots/list`) | Server cannot initiate requests to the client |
| **Server→client notifications** (`notifications/progress`, `notifications/message`, `notifications/tools/list_changed`, etc.) | Server cannot push out-of-band notifications |

These limitations are inherent to the Power Platform connector runtime and apply equally to v1 and v2.1 templates.

## License

MIT License - feel free to use and modify for your projects.
