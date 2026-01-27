# Power MCP Template with MCP Apps Support

A production-ready template for implementing [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) servers as Power Platform custom connectors. This enables AI agents in Copilot Studio to discover and use your connector's tools dynamically.

**Complete MCP Apps Reference Implementation** - All 5 UI components implement the full `@modelcontextprotocol/ext-apps` SDK including:
- All notification handlers (`ontoolinput`, `ontoolinputpartial`, `ontoolresult`, `ontoolcancelled`, `onhostcontextchanged`, `onteardown`, `oncalltool`, `onlisttools`, `onerror`)
- All SDK methods (`updateModelContext`, `sendMessage`, `callServerTool`, `requestDisplayMode`, `openLink`, `sendLog`, `sendSizeChanged`)
- App-side tool registration via `oncalltool` and `onlisttools`
- Theme support with CSS variables and `applyDocumentTheme()`
- State persistence via `localStorage` and `onteardown`
- Dark/light mode styling

## Features

### Core MCP Protocol
- **Full MCP Compliance**: Implements JSON-RPC 2.0 and MCP Apps specification (2026-01-26)
- **Batch Request Support**: Process multiple JSON-RPC requests in a single call
- **15+ MCP Methods**: initialize, tools/list, tools/call, resources/list, resources/read, resources/templates/list, prompts/list, prompts/get, logging/setLevel, and more
- **Copilot Studio Compatible**: Tested and working with Microsoft Copilot Studio agents
- **Prompts**: 3 built-in prompts (analyze_data, summarize, code_review) with argument support
- **Resource Templates**: Dynamic URI patterns with parameter extraction (data://{type}/{id})
- **Logging Levels**: Runtime-adjustable log levels (debug, info, notice, warning, error, critical, alert, emergency)
- **Cancellation Support**: Track and cancel in-progress operations

### MCP Apps UI Components (5 Built-in)
- **Color Picker**: Interactive color selection with history, theme support, and all SDK methods
- **Data Visualizer**: Chart.js-powered charts (bar, line, pie, doughnut) with click selection
- **Form Input**: Dynamic forms with validation, draft persistence, and field manipulation
- **Data Table**: Sortable/filterable tables with row selection and bulk export
- **Confirmation Dialog**: Confirm/cancel dialogs with info/warning/danger variants

### Developer Experience
- **Typed Argument Helpers**: `GetIntArgument`, `GetBoolArgument`, `GetDateTimeArgument`, `GetArrayArgument`, `GetObjectArgument`
- **Query String Builder**: `BuildQueryString()` for clean URL construction
- **Retry Logic**: Exponential backoff for transient failures (429, 502, 503, 504)
- **Structured Content**: Returns both `content` (text) and `structuredContent` (typed data) for MCP Apps

### Operations
- **Application Insights Integration**: Optional telemetry for monitoring and debugging
- **External API Helpers**: `SendGetRequestAsync`, `SendPostRequestAsync`, `SendDeleteRequestAsync`
- **Comprehensive Logging**: Context.Logger, correlation IDs, and runtime log level control
- **Operation Tracking**: Track in-progress operations with cancellation support

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

### String Arguments

#### RequireArgument
Get a required argument, throws `ArgumentException` if missing:
```csharp
var value = RequireArgument(arguments, "paramName");
```

#### GetArgument
Get an optional argument with default value:
```csharp
var value = GetArgument(arguments, "paramName", "defaultValue");
```

### Typed Arguments

#### GetIntArgument / RequireIntArgument
```csharp
var count = GetIntArgument(arguments, "count", 10);  // Optional with default
var id = RequireIntArgument(arguments, "id");         // Required, throws if missing
```

#### GetBoolArgument
```csharp
var enabled = GetBoolArgument(arguments, "enabled", false);
// Accepts: true/false, "true"/"false", "1"/"0", "yes"/"no"
```

#### GetDateTimeArgument
```csharp
var date = GetDateTimeArgument(arguments, "startDate", DateTime.UtcNow);
// Parses ISO 8601 and common date formats
```

#### GetArrayArgument
```csharp
var items = GetArrayArgument(arguments, "items");
// Returns JArray, parses JSON string if needed
```

#### GetObjectArgument
```csharp
var config = GetObjectArgument(arguments, "config");
// Returns JObject, parses JSON string if needed
```

### Connection Parameters

#### GetConnectionParameter
Get a connection parameter from the connector:
```csharp
var apiKey = GetConnectionParameter("apiKey");
```

### External API Calls

#### SendExternalRequestAsync
Make external API calls with retry logic:
```csharp
// GET request
var result = await SendGetRequestAsync("https://api.example.com/data");

// POST with body
var result = await SendPostRequestAsync("https://api.example.com/data", new JObject { ["key"] = "value" });

// DELETE request
var result = await SendDeleteRequestAsync("https://api.example.com/data/123");

// Full control (method, body, retries, delay)
var result = await SendExternalRequestAsync(
    HttpMethod.Put,
    "https://api.example.com/data/123",
    body: payload,
    maxRetries: 3,
    retryDelayMs: 500  // Doubles each retry (exponential backoff)
);
```

**Retry Logic**: Automatically retries on 429 (rate limit), 502, 503, 504 with exponential backoff.

### URL Building

#### BuildQueryString
Build query strings from parameters (skips null/empty values):
```csharp
var url = "https://api.example.com/search" + BuildQueryString(
    ("q", searchTerm),
    ("limit", "10"),
    ("offset", cursor)  // Skipped if null
);
// Result: https://api.example.com/search?q=hello&limit=10
```

## Infrastructure Helpers

### Caching

Simple in-memory cache with TTL for reducing repeated API calls:

```csharp
// Get cached or fetch (recommended pattern)
var data = await GetCachedOrFetchAsync(
    "cache-key",
    async () => await FetchDataFromApi(),
    ttlSeconds: 300  // 5 minutes
);

// Manual cache operations
SetInCache("key", myData, ttlSeconds: 600);  // 10 minutes
var cached = GetFromCache("key");             // Returns null if expired
RemoveFromCache("key");                       // Explicit removal
CleanupExpiredCache();                        // Housekeeping
```

**Note:** Cache is in-memory and lost when connector recycles. For production high-availability, consider external cache (Redis, Cosmos DB).

### Pagination

Cursor-based pagination for large datasets:

```csharp
// Create paginated response
var allItems = new JArray { /* many items */ };
var result = CreatePaginatedResponse(allItems, cursor, pageSize: 20);
// Returns: { items: [...], total: N, hasMore: true, nextCursor: "..." }

// Encode/decode cursors
var cursor = EncodeCursor("100");     // Base64 encoded
var value = DecodeCursor(cursor);     // "100"
```

### Image & Binary Content

Return images and binary data in MCP tool results:

```csharp
// Create image content from base64
var imageContent = CreateImageContent(base64Data, "image/png");

// Fetch image from URL and convert to MCP content
var imageContent = await FetchImageAsContentAsync("https://example.com/image.png");

// Create resource content for other binary types
var pdfContent = CreateResourceContent(base64Pdf, "application/pdf", "file://report.pdf");

// Rich tool result with text + image
var content = CreateRichToolResult(
    "Here's your chart:",
    imageBase64,
    "image/png"
);
```

#### Using in Tool Results

```csharp
private async Task<JObject> ExecuteChartToolAsync(JObject arguments)
{
    // Generate or fetch chart image
    var chartImage = await FetchImageAsContentAsync("https://quickchart.io/chart?c={...}");
    
    return new JObject
    {
        ["description"] = "Chart generated successfully",
        ["_content"] = new JArray
        {
            new JObject { ["type"] = "text", ["text"] = "Sales data visualization:" },
            chartImage
        }
    };
}
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
| `notifications/cancelled` | Cancel in-progress operations |
| `ping` | Health check |
| `tools/list` | List available tools (supports `cursor`, ignored for static list) |
| `tools/call` | Execute a tool |
| `resources/list` | List resources including UI resources |
| `resources/read` | Read resource content (serves `ui://`, `data://`, `config://`, `log://`) |
| `resources/templates/list` | List dynamic resource templates |
| `prompts/list` | List available prompts with arguments |
| `prompts/get` | Get prompt content with argument substitution |
| `completion/complete` | Auto-completion (returns empty) |
| `logging/setLevel` | Set runtime logging level |

## Prompts

The template includes a full prompts implementation with argument support.

### Built-in Prompts

| Prompt | Description | Arguments |
|--------|-------------|-----------|
| `analyze_data` | Analyze data with context | `dataType`, `context` |
| `summarize` | Generate summary | `length` (short/medium/long), `style` (professional/casual/technical) |
| `code_review` | Review code | `language`, `focus` (security/performance/readability/general) |

### Adding Custom Prompts

Add to `BuildPromptsList()`:

```csharp
new JObject
{
    ["name"] = "my_prompt",
    ["description"] = "Description for the AI",
    ["arguments"] = new JArray
    {
        new JObject
        {
            ["name"] = "param1",
            ["description"] = "What this parameter does",
            ["required"] = true  // or false for optional
        }
    }
}
```

### Testing Prompts

```bash
# List available prompts
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"prompts/list","id":1}'

# Get a prompt with arguments
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"prompts/get","id":2,"params":{"name":"analyze_data","arguments":{"dataType":"sales data","context":"Q4 performance"}}}'
```

## Resource Templates

Resource templates define dynamic URI patterns with parameter extraction.

### Built-in Templates

| Template | Pattern | Description |
|----------|---------|-------------|
| Data Resource | `data://{dataType}/{id}` | Access data by type and ID |
| Configuration | `config://{section}` | Access config sections |
| Log Entries | `log://{level}/{count}` | Retrieve log entries |

### Adding Custom Templates

Add to `BuildResourceTemplatesList()`:

```csharp
new JObject
{
    ["uriTemplate"] = "myscheme://{param1}/{param2}",
    ["name"] = "My Resource",
    ["description"] = "Description of the resource",
    ["mimeType"] = "application/json"
}
```

Then handle in `HandleResourcesReadAsync()` by parsing the URI and extracting parameters.

### Testing Resource Templates

```bash
# List templates
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"resources/templates/list","id":1}'

# Read a templated resource
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"resources/read","id":2,"params":{"uri":"data://users/123"}}'
```

## Logging Levels

Runtime-adjustable logging levels per MCP specification.

### Available Levels

| Level | Value | Description |
|-------|-------|-------------|
| `debug` | 0 | Verbose debugging information |
| `info` | 1 | General operational information |
| `notice` | 2 | Normal but significant conditions |
| `warning` | 3 | Warning conditions |
| `error` | 4 | Error conditions |
| `critical` | 5 | Critical conditions |
| `alert` | 6 | Action must be taken immediately |
| `emergency` | 7 | System is unusable |

### Setting Log Level

```bash
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"logging/setLevel","id":1,"params":{"level":"debug"}}'
```

### Using in Code

```csharp
// Check if logging is enabled at a level
if (ShouldLog("debug"))
{
    this.Context.Logger.LogDebug("Detailed debug info...");
}
```

## Cancellation

Track and cancel in-progress operations.

### How It Works

1. Each tool call is tracked with a unique operation ID
2. Clients can send `notifications/cancelled` with the operation ID
3. The server sets a cancellation token that tools can check
4. Tools should periodically check `this.CancellationToken.IsCancellationRequested`

### Testing Cancellation

```bash
# Start a long-running tool (in one terminal)
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"tools/call","id":"op-123","params":{"name":"long_running_tool","arguments":{}}}'

# Cancel the operation (in another terminal)
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":"op-123","reason":"User cancelled"}}'
```

### In Tool Implementation

```csharp
private async Task<JObject> ExecuteLongRunningToolAsync(JObject arguments)
{
    for (int i = 0; i < 100; i++)
    {
        // Check for cancellation periodically
        if (this.CancellationToken.IsCancellationRequested)
        {
            return new JObject
            {
                ["status"] = "cancelled",
                ["progress"] = i
            };
        }
        
        await Task.Delay(100).ConfigureAwait(false);
    }
    
    return new JObject { ["status"] = "completed" };
}

## MCP Apps - Interactive UI Components

MCP Apps enables tools to return rich, interactive UI components that render directly in the conversation. This is useful for color pickers, data visualizations, forms, dashboards, and more.

### How It Works

1. **Tool declares UI resource**: Add `_meta.ui.resourceUri` to your tool definition pointing to a `ui://` URI
2. **Server serves UI content**: The `resources/read` handler returns bundled HTML/JS for the URI
3. **Host renders in iframe**: The client renders the UI in a sandboxed iframe
4. **Bidirectional communication**: The UI uses `@modelcontextprotocol/ext-apps` SDK to communicate back

### Adding a UI Tool

#### 1. Define the Tool with `_meta.ui`

```csharp
new JObject
{
    ["name"] = "my_ui_tool",
    ["description"] = "Tool with interactive UI",
    ["inputSchema"] = new JObject { ... },
    ["_meta"] = new JObject
    {
        ["ui"] = new JObject
        {
            ["resourceUri"] = "ui://my-component"
        }
    }
}
```

#### 2. Register the UI Resource

Add to `BuildUIResourcesList()`:

```csharp
new JObject
{
    ["uri"] = "ui://my-component",
    ["name"] = "My Component",
    ["description"] = "Description of the UI component",
    ["mimeType"] = "text/html"
}
```

#### 3. Add to GetUIResourceContent()

```csharp
case "ui://my-component":
    return GetMyComponentUI();
```

#### 4. Create the UI HTML/JS

```csharp
private string GetMyComponentUI()
{
    return @"<!DOCTYPE html>
<html>
<head>
    <script type=""module"">
        import { App } from 'https://esm.sh/@modelcontextprotocol/ext-apps@1.0.1';
        
        const app = new App();
        
        // Receive tool result data
        app.ontoolresult = (result) => {
            // Use result data to populate UI
            console.log('Received:', result);
        };
        
        // Send context update back to conversation
        window.sendUpdate = async function(data) {
            await app.updateModelContext({
                content: [{ type: 'text', text: `User action: ${data}` }]
            });
        };
        
        await app.connect();
    </script>
</head>
<body>
    <button onclick=""sendUpdate('clicked')"">Click Me</button>
</body>
</html>";
}
```

### Included UI Tools

| Tool | Description | UI Resource |
|------|-------------|-------------|
| `color_picker` | Interactive color selection with hex preview | `ui://color-picker` |
| `data_visualizer` | Chart.js charts (bar, line, pie, doughnut) | `ui://data-visualizer` |
| `form_input` | Dynamic forms with field types and validation | `ui://form-input` |
| `data_table` | Sortable table with row selection and export | `ui://data-table` |
| `confirm_action` | Confirmation dialog (info/warning/danger) | `ui://confirm-action` |

### Non-UI Tools

| Tool | Description |
|------|-------------|
| `echo` | Test connectivity by echoing a message |
| `get_data` | Example external API call pattern |

### MCP Apps SDK Reference (@modelcontextprotocol/ext-apps)

The `@modelcontextprotocol/ext-apps` SDK provides bidirectional communication between UI components and MCP hosts. **All 5 UI components in this template implement the complete SDK.**

#### App Initialization

```javascript
import { 
    App,
    applyDocumentTheme,
    applyHostStyleVariables,
    applyHostFonts
} from 'https://esm.sh/@modelcontextprotocol/ext-apps@1.0.1';

// Declare tool capability if app provides tools
const app = new App(
    { name: 'MyApp', version: '1.0.0' },
    { tools: { listChanged: false } }  // Optional: enable app-side tools
);
```

#### App Class Methods

| Method | Description | Implemented |
|--------|-------------|-------------|
| `app.connect()` | Initialize connection to host, establish handshake | ✅ All UIs |
| `app.getHostContext()` | Get theme, locale, displayMode, toolInfo | ✅ All UIs |
| `app.getHostCapabilities()` | Get host capabilities (openLinks, styles, tools) | ✅ All UIs |
| `app.getHostVersion()` | Get host implementation info (name, version) | ✅ All UIs |
| `app.updateModelContext()` | Update context sent to model (no response triggered) | ✅ All UIs |
| `app.sendMessage()` | Send message to chat (triggers model response) | ✅ All UIs |
| `app.callServerTool()` | Call a tool on the MCP server from the UI | ✅ All UIs |
| `app.requestDisplayMode()` | Change display (inline/fullscreen/pip) | ✅ Color Picker |
| `app.openLink()` | Request host to open external URL | ✅ All UIs |
| `app.sendLog()` | Send log to host for debugging | ✅ Color Picker |
| `app.sendSizeChanged()` | Notify host of iframe size change | ✅ All UIs |

#### Notification Handlers (set before connect)

| Handler | Description | Implemented |
|---------|-------------|-------------|
| `app.ontoolinput` | Complete tool arguments received (before result) | ✅ All UIs |
| `app.ontoolinputpartial` | Streaming partial tool arguments | ✅ All UIs |
| `app.ontoolresult` | Tool execution results from server | ✅ All UIs |
| `app.ontoolcancelled` | Tool execution was cancelled | ✅ All UIs |
| `app.onhostcontextchanged` | Theme/locale/displayMode changed | ✅ All UIs |
| `app.onteardown` | Graceful shutdown request, save state | ✅ All UIs |
| `app.oncalltool` | Handle tool calls from host (app-side tools) | ✅ All UIs |
| `app.onlisttools` | Return available tools (app-side tools) | ✅ All UIs |
| `app.onerror` | Error handler | ✅ All UIs |

#### Helper Functions

| Function | Description | Implemented |
|----------|-------------|-------------|
| `applyDocumentTheme()` | Apply theme ('light'/'dark') to document | ✅ All UIs |
| `applyHostStyleVariables()` | Apply CSS custom properties from host | ✅ All UIs |
| `applyHostFonts()` | Apply font CSS from host | ✅ All UIs |
| `buildAllowAttribute()` | Build iframe allow attribute | N/A (Host) |

### UI Component App-Side Tools

Each UI component exposes tools that the host can call:

| Component | Tools |
|-----------|-------|
| **Color Picker** | `get_current_color`, `set_color`, `get_color_history`, `clear_history` |
| **Data Visualizer** | `get_chart_data`, `set_chart_type`, `add_data_point`, `get_selected_point` |
| **Form Input** | `get_form_data`, `set_field_value`, `validate_form`, `clear_form` |
| **Data Table** | `get_selected_rows`, `select_row`, `clear_selection`, `sort_by`, `filter` |
| **Confirm Action** | `get_response`, `reset_dialog`, `set_variant` |

### UI Features

All UI components include:

- **Dark/Light Theme Support**: CSS variables adapt to host theme via `onhostcontextchanged`
- **State Persistence**: localStorage saves state between sessions via `onteardown`
- **Size Notifications**: `sendSizeChanged()` called on content changes
- **Interactive Demo Buttons**: Test SDK methods directly in the UI
- **Streaming Support**: `ontoolinputpartial` for progressive rendering

**Note:** Host support for SDK methods varies. Copilot Studio currently supports `connect()`, `ontoolinput`, `ontoolresult`, `updateModelContext()`, and `onerror`. Other methods are implemented and ready for when host support is enabled.

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
  "consumes": ["application/json"],
  "produces": ["application/json"],
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
  -d '{"jsonrpc":"2.0","method":"initialize","id":1,"params":{"protocolVersion":"2026-01-26","clientInfo":{"name":"test"}}}'

# List tools
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"tools/list","id":2}'

# List UI resources
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"resources/list","id":3}'

# Read a UI resource
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"resources/read","id":4,"params":{"uri":"ui://color-picker"}}'

# Call a tool
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"tools/call","id":5,"params":{"name":"echo","arguments":{"message":"Hello"}}}'

# Call a UI tool
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"tools/call","id":6,"params":{"name":"data_visualizer","arguments":{"chartType":"bar","title":"Sales"}}}'
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

## Batch Requests

The template supports JSON-RPC 2.0 batch requests - send multiple MCP calls in one HTTP request:

```bash
curl -X POST https://your-connector/mcp \
  -H "Content-Type: application/json" \
  -d '[
    {"jsonrpc":"2.0","method":"tools/list","id":1},
    {"jsonrpc":"2.0","method":"resources/list","id":2}
  ]'
```

Returns an array of responses in the same order.

## Structured Content (MCP Apps)

Tool results include both `content` (text for LLM) and `structuredContent` (typed data for UI):

```json
{
  "content": [{ "type": "text", "text": "{\"color\": \"#3B82F6\"}" }],
  "structuredContent": { "color": "#3B82F6" },
  "isError": false
}
```

MCP Apps UIs receive `structuredContent` directly without parsing JSON strings.

## Notes on Contract

- The swagger intentionally omits body parameters and response schemas; the MCP payload is forwarded by the connector runtime.
- Keep `x-ms-agentic-protocol` set to `mcp-streamable-1.0`.

## License

MIT License - feel free to use and modify for your projects.

