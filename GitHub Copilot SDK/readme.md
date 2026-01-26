# GitHub Copilot SDK Connector

MCP-compliant custom connector that proxies JSON-RPC to the **GitHub Copilot SDK** via Cloudflare Tunnel.

## Architecture

```
Power Platform → Cloudflare Tunnel → localhost:3000 → GitHub Copilot SDK
```

## Setup

### 1. Install dependencies
```bash
cd "GitHub Copilot SDK"
npm install
```

### 2. Start the proxy server
```bash
npm start
# or: node proxy-http-to-sdk.mjs
```

### 3. Start Cloudflare Tunnel
```bash
cloudflared tunnel --url http://localhost:3000
```
Copy the generated URL (e.g., `https://abc-xyz.trycloudflare.com`).

### 4. Update the connector script
Edit `script.csx` and set `DefaultSdkUrl` to your tunnel URL + `/jsonrpc`:
```csharp
private const string DefaultSdkUrl = "https://abc-xyz.trycloudflare.com/jsonrpc";
```

### 5. Deploy to Power Platform
```bash
pac connector update --environment <env-id> --connector-id <connector-id> --script-file script.csx
```

## Tools

| Tool | JSON-RPC | Description |
|------|----------|-------------|
| `copilot_create_session` | `session.create` | Create a new session (optional: `model`, `sessionId`) |
| `copilot_send` | `session.send` | Send prompt to a session |
| `copilot_resume_session` | `session.resume` | Resume an existing session |
| `copilot_list_sessions` | `session.list` | List all sessions |
| `copilot_delete_session` | `session.delete` | Delete a session |
| `copilot_ping` | `ping` | Ping the server |
| `copilot_list_models` | `models.list` | List available models |
| `copilot_get_status` | `status.get` | Get server status |
| `copilot_get_auth_status` | `auth.status` | Get authentication status |

## Example: Create Session

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "id": 1,
  "params": {
    "name": "copilot_create_session",
    "arguments": {
      "model": "claude-sonnet-4",
      "sessionId": "my-session"
    }
  }
}
```

## Authentication

The connector uses your local GitHub Copilot authentication. Run `copilot auth status` to verify you're authenticated.

## Notes

- **Tunnel URL changes on restart**: Quick tunnels generate a new URL each time. Update `script.csx` and redeploy when restarting.
- **Sessions persist locally**: The SDK stores sessions on disk.
- **No connection parameters needed**: The URL is hardcoded in the script for simplicity.

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Connection refused | Ensure `node proxy-http-to-sdk.mjs` is running |
| Tunnel not reachable | Verify `cloudflared tunnel` is running and URL is updated in script |
| Auth required | Run `copilot auth login` on your machine |

## Files

| File | Purpose |
|------|--------|
| `proxy-http-to-sdk.mjs` | HTTP JSON-RPC server using GitHub Copilot SDK |
| `script.csx` | Power Platform connector script |
| `apiDefinition.swagger.json` | OpenAPI definition |
| `apiProperties.json` | Connector properties |
