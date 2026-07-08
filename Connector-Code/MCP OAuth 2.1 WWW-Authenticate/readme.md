# MCP OAuth 2.1 WWW-Authenticate Handler

Reusable template for Power Platform custom connectors that connect to MCP servers implementing the [2025-11-25 MCP Authorization spec](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization).

## Files

| File | Purpose |
|------|---------|
| `script.csx` | Custom code that intercepts 401/403 responses, parses `WWW-Authenticate`, discovers auth endpoints via RFC 9728, and returns actionable diagnostics |
| `apiDefinition.swagger.json` | Template Swagger with MCP endpoint (`/mcp`) + metadata discovery endpoint (`/.well-known/oauth-protected-resource`). Dual-mode ready |
| `apiProperties.json` | Template for **non-Azure AD** servers using `oauth2generic` identity provider |
| `apiProperties.aad.json` | Template for **Azure AD/Entra ID** servers using `aad` identity provider. Rename to `apiProperties.json` when deploying |

## Problem

MCP servers following OAuth 2.1 return `401 Unauthorized` with a `WWW-Authenticate` header containing:
- `resource_metadata` -- URL to the [Protected Resource Metadata](https://datatracker.ietf.org/doc/html/rfc9728) document
- `scope` -- required scopes for the operation

Power Platform's built-in OAuth doesn't natively handle this dynamic authorization discovery flow. This code bridges that gap.

## What It Does

1. **Parses `WWW-Authenticate` headers** (RFC 6750) from 401 and 403 responses
2. **Fetches Protected Resource Metadata** (RFC 9728) to discover the authorization server
3. **Fetches Authorization Server Metadata** (RFC 8414 / OIDC Discovery) to find token and authorization endpoints
4. **Detects Azure AD/Entra ID** servers automatically (recognizes `api://` scopes, `login.microsoftonline.com` issuers)
5. **Validates token expiry** with a 5-minute buffer before attempting OBO exchange (per [ISE blog](https://devblogs.microsoft.com/ise/aca-secure-mcp-server-oauth21-azure-ad/) recommendation)
6. **Handles `insufficient_scope` errors** (403) for step-up authorization
7. **Returns actionable diagnostics** -- discovered endpoints, suggested `apiProperties.json` configuration, and Copilot Studio onboarding guidance
8. **Optionally retries** with a new token via client credentials or OBO flow

## When to Use This vs. Copilot Studio Native

| Scenario | Approach |
|----------|----------|
| MCP server supports DCR + discovery | Use Copilot Studio's **Dynamic discovery** OAuth type -- no custom connector needed |
| MCP server has known auth endpoints | Use Copilot Studio's **Manual** OAuth type -- no custom connector needed |
| Need typed Power Automate operations alongside MCP | Custom connector with this `script.csx` (dual-mode connector) |
| Auth server requires custom token exchange (OBO, ROPC) | Custom connector with this `script.csx` |
| Debugging 401s to discover what endpoints the MCP server expects | Custom connector with this `script.csx` -- diagnostics will reveal endpoints |

See [Connect your agent to an existing MCP server](https://learn.microsoft.com/en-us/microsoft-copilot-studio/mcp-add-existing-server-to-agent) for the Copilot Studio native options.

## How It Works with Power Platform Connections

Power Platform handles the full OAuth Authorization Code flow — the user signs in, consents, and the platform stores and refreshes tokens automatically. By the time `ExecuteAsync()` runs in `script.csx`, the `Authorization: Bearer {token}` header is already on the request.

**This script never sees the authorization code exchange.** It operates on the Bearer token that Power Platform already attached.

### Why 401s Happen with a Valid Token

The MCP server rejects the token because of a mismatch between what Power Platform sent and what the server expects:

| Root Cause | Example |
|-----------|---------|
| Wrong audience | `apiProperties.json` points to Auth Server A, but the MCP server wants tokens from Auth Server B |
| Missing scopes | Connector requests `openid profile`, but the MCP server requires `api://{id}/access_as_user` |
| Wrong identity provider | Used `oauth2generic` when the server needs `aad` (or vice versa) |
| No auth configured | Connector was deployed without auth to discover what the MCP server needs |

### What the Script Does for Authorization Code Flows

The script **cannot automatically fix a misconfigured Authorization Code flow** — that would require redirecting the user to a different auth server, which `script.csx` can't do. Instead, it acts as a **one-shot discovery tool**:

```
Power Platform sends request with Bearer token
         |
         v
MCP Server returns 401 + WWW-Authenticate
         |
         v
script.csx parses header, discovers endpoints
         |
         v
Returns diagnostic JSON with the correct values
for apiProperties.json so the NEXT deployment
gets the right token from the right auth server
```

**Workflow:** Deploy with wrong/no auth --> get 401 --> read `suggested_apiProperties_config` from the diagnostic response --> update `apiProperties.json` --> re-deploy --> create a new connection (user signs in to the correct auth server) --> 200 OK.

### The OBO Exception (Azure AD Only)

The one case where the script CAN automatically retry with Authorization Code tokens is the **On-Behalf-Of flow**. Power Platform gets a token for your connector's App Registration, and the script exchanges it at `login.microsoftonline.com` for a new token scoped to the MCP server:

```
Power Platform token (audience: connector's App Registration)
         |
         v  OBO exchange at login.microsoftonline.com
         |
         v
New token (audience: MCP server's App Registration)
         |
         v
Retry original request --> 200 OK
```

This requires both apps to be in the same Azure AD tenant (or multi-tenant), and your connector's App Registration must have API permissions for the MCP server's exposed scopes. Uncomment Option 2 in `AttemptTokenRefreshAsync()` and provide the connector's `client_id` and `client_secret`. No user re-authentication needed.

## Quick Start

### Step 1: Discover Your MCP Server's Auth Requirements

Before configuring anything, check if your MCP server exposes Protected Resource Metadata:

```bash
curl https://your-mcp-server.example.com/.well-known/oauth-protected-resource | jq .
```

If the endpoint exists, you'll get back a JSON document like:

```json
{
    "resource": "https://your-mcp-server.example.com",
    "bearer_methods_supported": ["header"],
    "authorization_servers": [
        "https://login.microsoftonline.com/{tenant-id}/v2.0"
    ],
    "scopes_supported": [
        "api://{client-id}/access_as_user"
    ]
}
```

If the endpoint doesn't exist, deploy the connector with no auth first. When the MCP server returns a 401, the `script.csx` will parse the `WWW-Authenticate` header and discover the endpoints for you.

### Step 2: Choose Your Auth Template

**Azure AD / Entra ID** (issuer contains `login.microsoftonline.com`):
1. Copy `apiProperties.aad.json` to your connector folder as `apiProperties.json`
2. Replace `[REPLACE_WITH_CLIENT_ID]` with your connector's App Registration Client ID
3. Replace `[MCP_SERVER_APP_ID]` with the MCP server's App Registration Client ID
4. In the MCP server's App Registration, go to **Expose an API > Authorized client applications** and add your connector's Client ID

**Non-Azure AD** (any other OAuth 2.0 / OIDC server):
1. Copy `apiProperties.json` to your connector folder
2. Replace `[REPLACE_WITH_CLIENT_ID]` with the client ID from your OAuth provider
3. Replace `YOUR_AUTH_SERVER` URLs with the `authorization_endpoint` and `token_endpoint` from the auth server's metadata

### Step 3: Configure the Swagger

1. Copy `apiDefinition.swagger.json` to your connector folder
2. Replace `host` with your MCP server's hostname
3. Replace the `securityDefinitions` OAuth URLs with values from Step 1 or Step 2
4. If building a dual-mode connector, add your typed operation paths alongside the `/mcp` path

### Step 4: Integrate the Script

Copy the helper methods from `script.csx` into your connector's `script.csx`. In your `ExecuteAsync()`, wrap your outbound call:

```csharp
public override async Task<HttpResponseMessage> ExecuteAsync()
{
    // Your existing setup code...

    // Instead of:
    // var response = await this.Context.SendAsync(request, this.CancellationToken);

    // Use:
    var response = await SendWithMcpAuthAsync(this.Context.Request);
    return response;
}
```

### Step 5: Deploy and Test

```bash
# Deploy to Power Platform
paconn create -s settings.json -e c4f149b0-9f42-e8c4-97d8-bc69b59f971c

# Or validate first
ppcv ./YourConnectorFolder
```

When you test and get a 401, the diagnostic response will contain everything you need:

```json
{
    "error": "mcp_authorization_required",
    "suggested_apiProperties_config": {
        "identityProvider": "aad",
        "authorizationUrlTemplate": "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize",
        "tokenUrlTemplate": "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
        "scopes": "api://{app-id}/access_as_user",
        "copilot_studio_options": {
            "dynamic_discovery": "Not supported",
            "manual_config": { ... }
        }
    }
}
```

## Configuring Token Refresh

The script includes three commented-out token acquisition patterns in `AttemptTokenRefreshAsync()`. Uncomment the one that matches your scenario:

### Option 1: Client Credentials (machine-to-machine)

For MCP servers that accept app-only tokens. Provide your `client_id` and `client_secret`:

```csharp
var formData = new Dictionary<string, string>
{
    { "grant_type", "client_credentials" },
    { "client_id", clientId },
    { "client_secret", clientSecret },
    { "scope", requiredScopes },
    { "resource", resourceUri }  // RFC 8707
};
```

### Option 2: Azure AD On-Behalf-Of (OBO)

For MCP servers behind Azure AD where you need to exchange the Power Platform user's token for one scoped to the MCP server. Requires:
- The MCP server's App Registration to expose API scopes (`api://{client-id}/{scope-name}`)
- Your connector's App Registration to have API permissions for those scopes
- See [OBO flow docs](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)

```csharp
var oboForm = new Dictionary<string, string>
{
    { "grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer" },
    { "client_id", clientId },
    { "client_secret", clientSecret },
    { "assertion", existingToken },
    { "scope", requiredScopes },
    { "requested_token_use", "on_behalf_of" }
};
```

### Option 3: Token Exchange (RFC 8693)

For non-standard auth servers that support trading one token for another. This is uncommon.

## Reading Diagnostics

When the handler can't auto-recover from a 401, it returns a JSON diagnostic payload:

```json
{
  "error": "mcp_authorization_required",
  "message": "The MCP server requires OAuth 2.1 authorization...",
  "www_authenticate": {
    "scope": "api://58348f96-645d-4a2e-xxxx/access_as_user",
    "resource_metadata_url": "https://mcp.example.com/.well-known/oauth-protected-resource"
  },
  "authorization_server_metadata": {
    "issuer": "https://login.microsoftonline.com/{tenant-id}/v2.0",
    "is_azure_ad": true,
    "authorization_endpoint": "https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/authorize",
    "token_endpoint": "https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token",
    "scopes_supported": ["api://58348f96-645d-4a2e-xxxx/access_as_user"]
  },
  "suggested_apiProperties_config": {
    "identityProvider": "aad",
    "authorizationUrlTemplate": "https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/authorize",
    "tokenUrlTemplate": "https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token",
    "refreshUrlTemplate": "https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token",
    "scopes": "api://58348f96-645d-4a2e-xxxx/access_as_user",
    "tenantId": "{tenant-id}",
    "AzureActiveDirectoryResourceId": "api://58348f96-645d-4a2e-xxxx",
    "resourceUri": "api://58348f96-645d-4a2e-xxxx",
    "copilot_studio_options": {
      "dynamic_discovery": "Not supported -- use 'Manual' OAuth type and provide endpoints below",
      "manual_config": {
        "authorization_url": "https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/authorize",
        "token_url": "https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token"
      }
    }
  }
}
```

Use the `suggested_apiProperties_config` values to update your connector's `apiProperties.json` OAuth settings. The `copilot_studio_options` section tells you which OAuth type to pick in the MCP onboarding wizard.

## MCP Authorization Flow

```
Client Request --> MCP Server
                    | 401 + WWW-Authenticate: Bearer resource_metadata="..."
                    v
Parse WWW-Authenticate header
                    |
                    v
Fetch /.well-known/oauth-protected-resource (RFC 9728)
                    |
                    v
Extract authorization_servers[]
                    |
                    v
Fetch /.well-known/oauth-authorization-server (RFC 8414)
  or /.well-known/openid-configuration (OIDC Discovery)
                    |
                    v
Discover: authorization_endpoint, token_endpoint, scopes_supported
                    |
                    v
Acquire token --> Retry original request with Bearer token
```

## Common Troubleshooting

### 401 with no WWW-Authenticate header
The MCP server isn't following the spec. Check if it requires an API key instead, or if auth is configured at the infrastructure level (APIM, App Service auth, etc.) rather than in the MCP server itself.

### 401 with WWW-Authenticate but no resource_metadata
The server uses RFC 6750 Bearer challenges but hasn't implemented RFC 9728. The script will fall back to well-known URI discovery at `/.well-known/oauth-protected-resource`.

### Scopes show tenant ID instead of client ID
A common misconfiguration per the [ISE blog](https://devblogs.microsoft.com/ise/aca-secure-mcp-server-oauth21-azure-ad/). The MCP server's `/.well-known/oauth-protected-resource` should return scopes like `api://{CLIENT-ID}/scope`, not `api://{TENANT-ID}/scope`.

### Token expired during OBO exchange
The script validates the token's `exp` claim with a 5-minute buffer. If the token is expiring soon, it skips OBO and returns diagnostics. The user needs to re-authenticate the connection.

### Protected Resource Metadata returns localhost
The MCP server's `RESOURCE_SERVER_URL` environment variable needs to be set to the production URL. This requires a two-phase deployment -- deploy first to get the URL, then set the variable and redeploy.

## Key Standards

| Standard | Purpose |
|----------|---------|
| [RFC 6750](https://datatracker.ietf.org/doc/html/rfc6750) | Bearer token usage and WWW-Authenticate format |
| [RFC 8707](https://www.rfc-editor.org/rfc/rfc8707.html) | Resource Indicators (`resource` parameter) |
| [RFC 9728](https://datatracker.ietf.org/doc/html/rfc9728) | Protected Resource Metadata |
| [RFC 8414](https://datatracker.ietf.org/doc/html/rfc8414) | Authorization Server Metadata |
| [OAuth 2.1](https://datatracker.ietf.org/doc/html/draft-ietf-oauth-v2-1-13) | Core authorization framework |

## Azure AD/Entra ID Notes

Based on the [ISE Developer Blog](https://devblogs.microsoft.com/ise/aca-secure-mcp-server-oauth21-azure-ad/):

- **Scope format**: Azure AD uses `api://{client-id}/{scope-name}` (the `client-id` is the App Registration's Application ID, NOT the Tenant ID)
- **Protected Resource Metadata**: MCP servers must expose `/.well-known/oauth-protected-resource` with `authorization_servers` pointing to `https://login.microsoftonline.com/{tenant-id}/v2.0`
- **OBO flow**: When the MCP server needs to call downstream APIs (e.g., Graph) on behalf of the user, it exchanges the incoming token via On-Behalf-Of. The script includes a commented OBO pattern
- **Token expiry**: Always check the token's `exp` claim with a 5-minute buffer before OBO exchange to avoid mid-flow expiration
- **Pre-authorized clients**: In the MCP server's App Registration, go to **Expose an API > Authorized client applications** to pre-authorize the connector's client ID
- **Secretless production**: Use Managed Identity + Federated Identity Credentials instead of client secrets
- **Identity provider**: Use `aad` in `apiProperties.json` -- do NOT use `aadcertificate` (restricted to first-party connectors)
