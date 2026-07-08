# Authentication Configuration

This folder contains manifest snippets for each supported authentication type.
Copy the relevant `agentConnectors` block into your `manifest.json`.

## Which auth type to use

| Auth Type | Use When | User Experience |
|-----------|----------|-----------------|
| **OAuthPluginVault** | Your API uses OAuth 2.0 (recommended for production) | User completes one-time consent flow |
| **ApiKeyPluginVault** | Your API uses API keys or tokens | User provides key once |
| **Dynamic Client Registration** | Your MCP server supports RFC 7591 DCR | OAuth consent flow (auto-configured) |
| **None** | Public APIs, anonymous endpoints, or internal services | No auth prompt |

## How auth works in Cowork

1. **User-initiated:** Admins cannot sign in on behalf of users. Each user
   completes the auth flow themselves the first time they use the connector.
2. **One-time:** After the initial sign-in, Cowork remembers the authorization
   across all conversations until revoked.
3. **Enterprise Token Store:** The `referenceId` in your manifest points to
   credentials registered in the Microsoft Enterprise Token Store. You register
   these credentials when submitting your plugin through
   [Partner Center](https://partner.microsoft.com/).
4. **Secrets never in manifest:** OAuth client secrets and API keys are stored
   in the Token Store, not in your plugin package.

## Registering credentials

When you submit your plugin to the Microsoft 365 App Store through Partner Center,
you provide your OAuth client ID, client secret, authorization/token URLs, and
scopes. Partner Center stores these in the Enterprise Token Store and generates
the `referenceId` you use in your manifest.

The `referenceId` value is the OAuth client registration ID that you create when
you [register an OAuth client with Agents Toolkit](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api-plugin-authentication#register-an-oauth-client-with-agents-toolkit).
When registering, set the usage by organization to **Any Microsoft 365 Organization**
to ensure your plugin works across tenants.

For sideloaded plugins during development, you may need to work with your tenant
admin to pre-register credentials. The exact developer flow for sideloaded auth
is expected to evolve as Cowork exits Frontier preview.

## Dynamic Client Registration (DCR)

If your MCP server supports [RFC 7591 Dynamic Client Registration](https://datatracker.ietf.org/doc/html/rfc7591),
you can omit the `authorization` configuration entirely from your connector definition.
Cowork automatically creates an OAuth client on your plugin's behalf.

```json
"remoteMcpServer": {
    "mcpServerUrl": "https://api.example.com/mcp"
}
```

No `referenceId`, no client credentials in Partner Center — Cowork handles the
OAuth setup at runtime. This is the simplest path if your server already supports DCR.

See [`dcr-connector.json`](dcr-connector.json) for the full snippet.

## Handling auth in skills

Skills must handle the case where a user hasn't connected yet. When a tool call
fails due to authentication, Cowork prompts the user to sign in. Your skill
should:

- Not assume the connector is already authenticated
- Present a clear message if data can't be retrieved due to connection status
- Continue with any non-connector-dependent parts of the workflow

See the `## Handling Authentication` section in each skill template for specific
guidance.
