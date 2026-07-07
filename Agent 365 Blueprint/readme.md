# Agent 365 Blueprint

Power Platform custom connector for managing Agent 365 blueprints via Microsoft Graph. Provides typed operations for the Entra ID lifecycle that underpins Agent 365 agent governance — creating blueprints, configuring Work IQ permissions, managing secrets, and granting API access.

## Context

An [Agent 365 blueprint](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/) is an Entra ID application registration that serves as the enterprise template for compliant agents. Every agent instance inherits its blueprint's rules, ensuring consistent governance across Mail, Calendar, Teams, SharePoint, and other Microsoft 365 workloads.

The [Agent 365 CLI](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/agent-365-cli) (`a365 setup blueprint`, `a365 setup permissions`) automates these operations for developers. This connector provides the same capabilities in Power Automate for:

- **IT admins** automating agent provisioning at scale
- **Governance flows** that create blueprints with standard permissions when new teams/projects are created
- **Compliance monitoring** that audits blueprint permissions across the tenant

## Operations

| Operation | Description |
|-----------|-------------|
| **CreateBlueprint** | Create an Entra app registration as an Agent 365 blueprint |
| **GetBlueprint** | Retrieve blueprint app registration details |
| **UpdateBlueprint** | Update blueprint properties and permissions |
| **DeleteBlueprint** | Remove the blueprint from Entra ID |
| **CreateServicePrincipal** | Create the SP required before granting permissions |
| **GrantDelegatedPermissions** | Create OAuth2 delegated grants (e.g., Work IQ `Tools.ListInvoke.All`) |
| **ListBlueprintPermissionGrants** | List all delegated permission grants on the blueprint |
| **GrantApplicationPermissions** | Assign app roles for S2S auth scenarios |
| **ListApplicationPermissions** | List app role assignments on the blueprint |
| **CreateBlueprintSecret** | Generate a client secret for agent runtime auth |

## Relationship to Agent 365 MCP Connector

| Connector | Auth Target | Purpose |
|-----------|-------------|---------|
| **Agent 365 Blueprint** (this) | Microsoft Graph | Entra lifecycle — blueprints, permissions, identity |
| **Agent 365 MCP** | Agent 365 platform | Runtime — Work IQ tools, MCPManagement, AdminTools |

Together they cover the full Agent 365 lifecycle: Blueprint creates and governs the agent definition; MCP provides the runtime tool access.

## Prerequisites

1. **Azure AD App Registration** for this connector:
   - Redirect URI: `https://global.consent.azure-apim.net/redirect`
   - API permissions (Application):
     - `Application.ReadWrite.All` — create/manage app registrations
     - `DelegatedPermissionGrant.ReadWrite.All` — manage OAuth2 grants
     - `AppRoleAssignment.ReadWrite.All` — manage app role assignments
   - Admin consent granted for above permissions
2. **Roles**: The signed-in user needs:
   - **Agent ID Developer** (minimum) for blueprint creation
   - **Global Administrator** for OAuth2 permission grants

## Import & Deploy

1. Import via Maker portal → Custom connectors → Import OpenAPI (apiDefinition.swagger.json)
2. Security: Configure OAuth2 (AAD) with your app registration `clientId` and scope `https://graph.microsoft.com/.default`
3. Create a connection — admin consent is required for the Graph permissions

## Example: Create a Blueprint with MCP Permissions

```
1. CreateBlueprint → displayName: "Sales Agent Blueprint"
2. CreateServicePrincipal → appId: (from step 1 response)
3. GrantDelegatedPermissions → clientId: (SP from step 2), resourceId: (Work IQ Mail SP), scope: "Tools.ListInvoke.All"
4. GrantDelegatedPermissions → clientId: (SP from step 2), resourceId: (Work IQ Calendar SP), scope: "Tools.ListInvoke.All"
5. CreateBlueprintSecret → blueprintId: (from step 1), displayName: "Runtime Secret"
```

## Work IQ MCP Server Permission Model

The per-server permission model (current) uses individual Entra app registrations per MCP server:

| Server | Scope | Notes |
|--------|-------|-------|
| Work IQ Mail MCP | `Tools.ListInvoke.All` | Per-server delegated |
| Work IQ Calendar MCP | `Tools.ListInvoke.All` | Per-server delegated |
| Work IQ Teams MCP | `Tools.ListInvoke.All` | Per-server delegated |
| Work IQ Word MCP | `Tools.ListInvoke.All` | Per-server delegated |
| Work IQ Tools (metadata) | `McpServersMetadata.Read.All` | Shared metadata access |

Resolve server SP IDs in your tenant: `GET /v1.0/servicePrincipals?$filter=displayName eq 'Work IQ Mail MCP'`

## Notes

- All operations are idempotent — safe to retry
- Blueprint deletion does NOT cascade to agent instances; clean up instances separately
- The legacy shared-scope model (`McpServers.Mail.All` etc.) is deprecated; use per-server `Tools.ListInvoke.All`
- Secret values are only returned once on creation — store securely
