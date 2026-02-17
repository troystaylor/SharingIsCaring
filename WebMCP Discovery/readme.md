# WebMCP Discovery Connector

A Power Platform custom connector that discovers and executes WebMCP tools from any web page, with Playwright browser automation as a fallback.

## Architecture

```
Power Automate / Copilot Studio
            ↓
   WebMCP Discovery Connector
            ↓
    WebMCP Broker Service (Azure Container Apps)
            ↓
     Headless Chromium (Playwright)
            ↓
       Target Website
            ↓
    WebMCP tools OR Playwright fallback
```

## Prerequisites

- Azure subscription (for Container Apps deployment)
- Power Platform environment
- Docker (for building broker service)

## Deployment

### 1. Deploy the Broker Service

```bash
cd broker-service

# Build the Docker image
docker build -t webmcp-broker:latest .

# Push to your container registry
docker tag webmcp-broker:latest ghcr.io/your-org/webmcp-broker:latest
docker push ghcr.io/your-org/webmcp-broker:latest

# Deploy to Azure Container Apps
az deployment group create \
  --resource-group your-resource-group \
  --template-file infra/main.bicep \
  --parameters apiKey="your-secure-api-key" \
               containerImage="ghcr.io/your-org/webmcp-broker:latest"
```

### 2. Import the Connector

1. Go to Power Automate > Custom Connectors
2. Import from OpenAPI file: `apiDefinition.swagger.json`
3. Create a connection with:
   - **Broker Service URL**: `https://your-app.azurecontainerapps.io`
   - **API Key**: The key you set during deployment

## Operations

### Discovery

| Operation | Description |
|-----------|-------------|
| Discover Tools | Scan a URL for WebMCP tools or get Playwright fallback tools |

### Sessions

| Operation | Description |
|-----------|-------------|
| Create Session | Start a persistent browser session |
| Get Session | Check session status and available tools |
| Close Session | End session and release resources |
| Navigate | Go to a new URL within session |
| List Session Tools | Get tools available on current page |

### Execution

| Operation | Description |
|-----------|-------------|
| Call Tool (Session) | Execute a tool within a session |
| Execute Tool (Stateless) | One-shot tool execution without session |

### Authentication

| Operation | Description |
|-----------|-------------|
| Inject Authentication | Set cookies, localStorage, or headers |

### Utility

| Operation | Description |
|-----------|-------------|
| Take Screenshot | Capture current page state |

## MCP Tools

The connector exposes these tools for Copilot Studio agents:

### High-Level Tools
- `discover_tools` - Find available tools on a page
- `create_session` - Start a browser session
- `call_tool` - Execute any discovered tool
- `execute_stateless` - One-shot execution

### Playwright Fallback Tools
When a page doesn't implement WebMCP, these browser automation tools are available:

| Tool | Description |
|------|-------------|
| `browser_navigate` | Go to a URL |
| `browser_click` | Click an element |
| `browser_type` | Type into an input |
| `browser_select` | Select from dropdown |
| `browser_get_text` | Extract text content |
| `browser_get_attribute` | Get element attribute |
| `browser_screenshot` | Capture page |
| `browser_evaluate` | Run JavaScript |
| `browser_wait_for_selector` | Wait for element |
| `browser_scroll` | Scroll page/element |

### High-Level Action Tools
Simplified tools that combine multiple browser actions:

| Tool | Description |
|------|-------------|
| `browser_login` | Fill username + password and submit |
| `browser_fill_form` | Fill multiple fields by label/name mapping |
| `browser_search_page` | Type into search box and submit |
| `browser_checkout` | Multi-step form fill (shipping, payment) |

### Smart Selector Tools
Find and interact with elements using human-readable descriptions:

| Tool | Description |
|------|-------------|
| `browser_click_text` | Click element by visible text content |
| `browser_click_nearest` | Click element nearest to a reference element |
| `browser_smart_fill` | Fill inputs by matching visible labels |

### Error Recovery & Recording
Built-in retry logic and action recording:

| Tool | Description |
|------|-------------|
| `browser_auto_retry` | Execute action with auto-retry and scroll-into-view |
| `browser_record_actions` | Start recording all page interactions |
| `browser_replay_actions` | Replay a recorded action sequence |

## Example Flows

### Discover and Execute WebMCP Tools

```
1. Discover Tools from URL "https://example.com/app"
   ↓
2. Response shows WebMCP tools: ["search_products", "add_to_cart", "checkout"]
   ↓
3. Call Tool: search_products with input { "query": "laptop" }
   ↓
4. Response: { "products": [...] }
```

### Fallback to Playwright for Traditional Sites

```
1. Discover Tools from URL "https://legacy-site.com"
   ↓
2. Response: hasWebMCP = false, tools = [browser_click, browser_type, ...]
   ↓
3. Create Session with URL
   ↓
4. browser_type { selector: "#search", text: "laptop", submit: true }
   ↓
5. browser_get_text { selector: ".results-count" }
   ↓
6. Close Session
```

### Authenticated Session

```
1. Create Session for "https://app.example.com/login"
   ↓
2. Inject Auth: cookies from your auth flow
   ↓
3. Navigate to "/dashboard"
   ↓
4. List Session Tools (now shows authenticated tools)
   ↓
5. Call Tool: get_user_data
   ↓
6. Close Session
```

## WebMCP Specification

The connector looks for tools registered via the [WebMCP spec](https://webmachinelearning.github.io/webmcp/):

```javascript
// Website registers tools like this:
navigator.modelContext.registerTool({
  name: "search_products",
  description: "Search the product catalog",
  inputSchema: {
    type: "object",
    properties: {
      query: { type: "string", description: "Search query" }
    },
    required: ["query"]
  },
  handler: async (input) => {
    // Execute the search
    return await fetch(`/api/search?q=${input.query}`).then(r => r.json());
  }
});
```

When a site implements WebMCP, the connector calls these structured tools directly. When not available, it falls back to Playwright browser automation.

## Cost Considerations

- **Azure Container Apps**: Scale to zero when idle = ~$0 when not in use
- **Active usage**: ~$0.000016/vCPU-second, ~$0.000002/GiB-second
- **Typical session**: ~$0.01 for a 5-minute browser session
- Premium connector billing may apply in Power Platform

## Security

The broker service includes a full enterprise security stack:

### Authentication

Set `AUTH_MODE` environment variable:

| Mode | Description |
|------|-------------|
| `apikey` (default) | API key via `X-API-Key` header |
| `managed-identity` | Azure AD / Entra ID Bearer tokens only |
| `both` | Accepts either API key or Bearer token |

For managed identity, also set:
- `AZURE_TENANT_ID` - your tenant ID
- `AZURE_CLIENT_ID` - the app registration client ID (audience)

### Role-Based Access Control (RBAC)

Set `RBAC_ENABLED=true` and configure `API_KEYS` as a JSON mapping:

```json
{
  "admin_key123": "admin",
  "user_key456": "user",
  "viewer_key789": "viewer"
}
```

| Role | Capabilities |
|------|-------------|
| **admin** | Full access: all tools, recording, config |
| **user** | All tools except `browser_evaluate`, tracing |
| **viewer** | Read-only: screenshots, getText, getPage — no navigation |

Keys can also use prefix convention: `admin_`, `viewer_`, or default to `user`.

### URL Allowlisting (SSRF Protection)

Controls which domains the broker can navigate to.

| Env Var | Description |
|---------|-------------|
| `ALLOWED_DOMAINS` | Comma-separated allowlist (empty = allow all) |
| `BLOCKED_DOMAINS` | Comma-separated blocklist |

Internal/metadata endpoints are **always blocked**: localhost, 127.0.0.1, 169.254.169.254 (Azure IMDS), etc.

### Network Egress Control

Set `NETWORK_EGRESS_CONTROL=true` (default) to enforce URL allowlisting at the browser level using Playwright route interception. All outbound requests from the browser (including sub-resources, XHR, images) are checked against the allowlist.

### Data Redaction

Automatically masks sensitive data in tool results and logs.

| Env Var | Description |
|---------|-------------|
| `REDACTION_FIELDS` | Field names to mask (default: `password,ssn,credit_card,api_key,secret,token,authorization`) |
| `REDACTION_PATTERNS` | Additional regex patterns (comma-separated) |

Built-in patterns detect: credit card numbers, SSNs, emails, phone numbers, bearer tokens, API key values.

Screenshots automatically blur sensitive form fields (`input[type="password"]`, `[data-sensitive]`, etc.).

### Audit Logging

Set `AUDIT_LOG_LEVEL`:

| Level | What's logged |
|-------|--------------|
| `none` | No logging |
| `basic` (default) | Method, path, status, duration |
| `detailed` | + tool names, success/fail, page changes |
| `full` | + full request/response metadata |

Set `AZURE_MONITOR_ENDPOINT` to send audit entries to Azure Monitor / Log Analytics.

### Session Recording

Set `SESSION_RECORDING=true` to record all tool executions in each session. Access recordings via:

```
GET /api/sessions/{sessionId}/recording
POST /api/sessions/{sessionId}/recording  { "enabled": true/false }
```

Each record includes: timestamp, toolName, input, success, durationMs, url, error.

### Private Networking (VNet)

Deploy with `enableVnet=true` in Bicep to:
- Create a VNet with Container Apps and private endpoint subnets
- Make the Container App internal-only (no public ingress)
- Set up private DNS zone for internal resolution

```bash
az deployment group create \
  --resource-group your-rg \
  --template-file infra/main.bicep \
  --parameters apiKey="your-key" \
               enableVnet=true \
               allowedDomains="example.com,contoso.com" \
               rbacEnabled=true \
               auditLogLevel="detailed"
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Session expired | Sessions have a 15-minute default TTL. Create a new session. |
| Tool not found | Use `discover_tools` first to see available tools on the page |
| Timeout errors | Increase timeout parameter or check if page is slow to load |
| Auth issues | Use `inject_auth` with cookies from a successful login flow |

## Alternative Hosting Options

> **Note on Microsoft Playwright Testing/Workspaces**: Microsoft Playwright Testing will be retired on March 8, 2026. If you were considering using it as a managed backend, you should instead create a Playwright Workspace in [Azure App Testing](https://learn.microsoft.com/azure/playwright-testing/), which is now generally available. This connector uses self-hosted Azure Container Apps by default.

| Option | Pros | Cons |
|--------|------|------|
| **Azure Container Apps** (default) | Scale-to-zero, full control, low cost | Self-managed |
| **Azure App Testing** | Microsoft-managed Playwright | Additional service dependency |
| **Azure Kubernetes Service** | Enterprise-grade, existing infrastructure | More complex setup |

## Resources

- [WebMCP Specification](https://webmachinelearning.github.io/webmcp/)
- [Playwright Documentation](https://playwright.dev/)
- [Azure Container Apps](https://docs.microsoft.com/azure/container-apps/)
- [Azure App Testing (Playwright)](https://learn.microsoft.com/azure/playwright-testing/)
- [Power Platform Custom Connectors](https://docs.microsoft.com/connectors/custom-connectors/)
