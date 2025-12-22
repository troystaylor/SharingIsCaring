# Outlook Mail MCP

Custom connector that exposes Outlook Mail (Microsoft Graph) actions via Model Context Protocol (MCP).

Only the following API operations are wrapped, per the provided spec:
- GET /users/{userId}/messages (list messages)
- GET /users/{userId}/messages/{messageId} (get a specific message)
- POST /users/{userId}/messages (create draft message)
- POST /users/{userId}/sendMail (send mail action)
- POST /users/{userId}/messages/{messageId}/reply (reply to a message)

## Files
- `apiDefinition.swagger.json` — OpenAPI 2.0 with a single `/mcp` operation and `x-ms-agentic-protocol: mcp-streamable-1.0`
- `apiProperties.json` — Connector metadata, `useBeta` connection parameter to switch Graph version
- `script.csx` — MCP JSON-RPC implementation with tools: `listMessages`, `getMessage`, `createMessage`, `sendMail`, `replyMessage`

## Beta compatibility
Set the `useBeta` connection parameter to route requests to `https://graph.microsoft.com/beta` instead of `v1.0`.

## Security
Configure OAuth 2.0 (Azure AD v2) in the connector Security tab with the Microsoft Graph scopes:
- `Mail.Read`
- `Mail.Send`

## Usage in Copilot Studio
Add this connector to your agent; Copilot detects the MCP endpoint and will call the tools based on intent. Provide inputs like `userId` (e.g., `me`), `top`, `filter`, and Graph `message` payloads as needed.

### Token optimization with `previewOnly`
To reduce token usage when reading messages, set the `previewOnly` parameter to `true` in `listMessages` and `getMessage` tools. This returns only `bodyPreview` (~255 chars) instead of the full `body`, significantly reducing the amount of data pulled from Microsoft Graph.

**Example:**
- `previewOnly: true` → returns `bodyPreview` (short summary)
- `previewOnly: false` (default) → returns full `body` (can be large)

When `previewOnly` is set, the connector automatically adjusts the `$select` parameter to exclude `body` and include `bodyPreview`, unless you explicitly provide a custom `select` parameter.
