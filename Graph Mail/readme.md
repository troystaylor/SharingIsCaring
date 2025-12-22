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

**This connector is designed specifically for Copilot Studio agents.** When you add it to your agent:

1. **Tool Discovery**: Copilot Studio automatically detects the MCP endpoint (marked with `x-ms-agentic-protocol: mcp-streamable-1.0`) and calls `tools/list` to discover available tools
2. **Natural Language Mapping**: The agent uses tool descriptions to understand when to invoke each tool based on user intent (e.g., "show me my latest emails" → `listMessages`)
3. **Smart Invocation**: The agent determines parameter values from context and calls `tools/call` with appropriate arguments
4. **Response Processing**: Results are returned to the agent for natural language formatting and follow-up actions

### Token optimization with `previewOnly`

To avoid token limit issues with large email bodies, both `listMessages` and `getMessage` support a `previewOnly` parameter:

- **`previewOnly: true` (default)**: Returns `bodyPreview` (~255 characters) instead of the full `body` property. This is the recommended setting for agents to prevent token exhaustion.
- **`previewOnly: false`**: Returns the full `body` property when complete email content is needed.

The connector automatically adjusts the `$select` parameter based on `previewOnly` unless you provide a custom `select` value. The `true` default ensures your agents won't hit token limits on large emails unless you explicitly need full content.

**Example:**
- `previewOnly: true` (default) → returns `bodyPreview` (short summary, ~255 chars)
- `previewOnly: false` → returns full `body` (can be megabytes)
