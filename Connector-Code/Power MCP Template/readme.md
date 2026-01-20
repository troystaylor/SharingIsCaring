# Power MCP Template

A template for implementing [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) servers as Power Platform custom connectors. This enables AI agents in Copilot Studio to discover and use your connector's tools dynamically.

## Features

- **Full MCP Compliance**: Implements JSON-RPC 2.0 and MCP specification (2025-11-25)
- **Copilot Studio Compatible**: Tested and working with Microsoft Copilot Studio agents
- **Application Insights Integration**: Optional telemetry for monitoring and debugging
- **External API Helper**: Built-in method for calling external APIs with authorization forwarding
- **Comprehensive Logging**: Uses Context.Logger and correlation IDs for traceability

## Quick Start

### 1. Configure Server Identity

Update the server configuration constants at the top of `script.csx`:

```csharp
private const string ServerName = "your-server-name";  // lowercase-with-dashes
private const string ServerVersion = "1.0.0";
private const string ServerTitle = "Your Server Title";
private const string ServerDescription = "Description of what your server does";
private const string ServerInstructions = ""; // Optional guidance returned in initialize
```

### 2. Define Your Tools

Add your tool definitions in `BuildToolsList()`:

```csharp
private JArray BuildToolsList()
{
    return new JArray
    {
        new JObject
        {
            ["name"] = "your_tool",
            ["description"] = "Clear description for the AI to understand when to use this tool",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["param1"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Description of this parameter"
                    }
                },
                ["required"] = new JArray { "param1" }
              },
              ["annotations"] = new JObject
              {
                ["readOnlyHint"] = true,
                ["idempotentHint"] = true,
                ["openWorldHint"] = false
              }
        }
    };
}
```

### 3. Route Tool Execution

Add your tool to the switch statement in `ExecuteToolAsync()`:

```csharp
private async Task<JObject> ExecuteToolAsync(string toolName, JObject arguments)
{
    switch (toolName.ToLowerInvariant())
    {
        case "your_tool":
            return await ExecuteYourToolAsync(arguments).ConfigureAwait(false);
        // ... other tools
    }
}
```

### 4. Implement Tool Logic

Create your tool implementation method:

```csharp
private async Task<JObject> ExecuteYourToolAsync(JObject arguments)
{
    var param1 = RequireArgument(arguments, "param1");
    
    // Your tool logic here
    
    return new JObject
    {
        ["result"] = "Your result"
    };
}
```

## Helper Methods

### RequireArgument
Get a required argument, throws `ArgumentException` if missing:
```csharp
var value = RequireArgument(arguments, "paramName");
```

### GetArgument
Get an optional argument with default value:
```csharp
var value = GetArgument(arguments, "paramName", "defaultValue");
```

### GetConnectionParameter
Get a connection parameter from the connector:
```csharp
var apiKey = GetConnectionParameter("apiKey");
```

### SendExternalRequestAsync
Make external API calls with automatic authorization forwarding:
```csharp
var result = await SendExternalRequestAsync(HttpMethod.Get, "https://api.example.com/data", null);
```

## Application Insights (Optional)

To enable telemetry:

1. Create an Application Insights resource in Azure Portal
2. Copy the connection string from Overview → Connection String
3. Set the `APP_INSIGHTS_CONNECTION_STRING` constant:

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = 
    "InstrumentationKey=xxx;IngestionEndpoint=https://region.in.applicationinsights.azure.com/;...";
```

### Tracked Events

| Event Name | Description |
|------------|-------------|
| McpRequestReceived | Incoming MCP request with method name |
| McpInitialized | Server initialization completed |
| McpToolsListed | Tools list requested |
| McpToolCallStarted | Tool execution started |
| McpToolCallCompleted | Tool execution completed |
| McpToolCallError | Tool execution failed |
| McpRequestCompleted | Request processing finished with duration |
| McpError | Unhandled error occurred |

## MCP Methods Supported

| Method | Description |
|--------|-------------|
| `initialize` | Server handshake and capability exchange |
| `initialized` / `notifications/initialized` | Client acknowledgment |
| `ping` | Health check |
| `tools/list` | List available tools (supports `cursor`, ignored for static list) |
| `tools/call` | Execute a tool |
| `resources/list` | List resources (returns empty) |
| `resources/templates/list` | List resource templates (returns empty) |
| `prompts/list` | List prompts (returns empty) |
| `completion/complete` | Auto-completion (returns empty) |
| `logging/setLevel` | Set logging level (acknowledged) |

## Connector Setup

### apiDefinition.swagger.json

> **Important:** Use this **exact** swagger contract. **No additional parameters** or **response schemas** are allowed.

```json
{
  "swagger": "2.0",
  "info": {
    "title": "MCP Server",
    "description": "This MCP Server will work with Streamable HTTP and is meant to work with Microsoft Copilot Studio",
    "version": "1.0.0"
  },
  "host": "your-api-host.com",
  "basePath": "/mcp",
  "schemes": [
    "https"
  ],
  "paths": {
    "/": {
      "post": {
        "summary": "MCP Server Streamable HTTP",
        "x-ms-agentic-protocol": "mcp-streamable-1.0",
        "operationId": "InvokeMCP",
        "responses": {
          "200": {
            "description": "Success"
          }
        }
      }
    }
  },
  "securityDefinitions": {}
}
```

### apiProperties.json

Configure the connector to use custom code:

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

## Testing

### Manual Testing with curl

```bash
# Initialize
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"initialize","id":1,"params":{"protocolVersion":"2025-11-25","clientInfo":{"name":"test"}}}'

# List tools
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"tools/list","id":2}'

# Call a tool
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"tools/call","id":3,"params":{"name":"echo","arguments":{"message":"Hello"}}}'
```

### Testing in Copilot Studio

1. Import the connector to your environment
2. Create a new agent in Copilot Studio
3. Add the connector as an action
4. The tools should appear automatically
5. Test by asking the agent to use your tools

## Error Handling

Tool errors are returned as MCP tool results (not protocol errors) so the AI can understand and react:
## Stateless Behavior

- `listChanged=false` for tools/resources/prompts (no notifications emitted)
- No server-side caching or shared state between requests
- `tools/list` accepts `cursor` but always returns full static list (no `nextCursor`)

## Notes on Contract

- The swagger intentionally omits parameters and schemas; the MCP payload is forwarded by the connector runtime.
- Keep `x-ms-agentic-protocol` set to `mcp-streamable-1.0`.
- Do not add `consumes`/`produces` or body parameters; Copilot Studio expects the exact contract above.


```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "content": [{"type": "text", "text": "Tool execution failed: reason"}],
    "isError": true
  }
}
```

## License

MIT License - feel free to use and modify for your projects.
