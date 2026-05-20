# Cowork Plugins

Production [Microsoft 365 Copilot Cowork](https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/) plugins that target third-party SaaS systems through an **in-tenant** MCP server (no vendor-hosted MCP in the data plane).

Each plugin in this folder is a standalone Cowork app package: `manifest.json`, `skills/`, icons, and a `package.ps1` that produces a sideload-ready `.zip`. Backing each one is a self-hosted MCP server (under `server/`) deployed to the customer Azure subscription via the Bicep in `infra/`.

## Plugins

| Plugin | Vendor | Read | Write | Status |
|---|---|---|---|---|
| [Slack/](Slack/readme.md) | Slack | Yes | Yes | Scaffolding |

## Architecture (shared across all plugins in this folder)

```
Copilot Cowork
   |  JSON-RPC 2.0 over HTTPS (streamable)
   |  + user OAuth token from M365 Enterprise Token Store
   v
APIM (your subscription)  --> Container Apps MCP server (your subscription)
                                |
                                |  Slack/Atlassian/etc. client secret from Key Vault
                                |  via managed identity
                                v
                            Vendor SaaS API (egress from your tenant)
```

**Boundary rules**

- The vendor's hosted MCP (Slack MCP, Atlassian Rovo MCP, etc.) is **never** in the path. The MCP server is hosted in the customer's Azure subscription.
- Vendor client secret lives only in Azure Key Vault, fetched by Container Apps via managed identity.
- All outbound calls to the vendor egress from the customer's Azure subscription so they can be logged, NAT'd, and SIEM'd.
- User OAuth tokens flow through the M365 Enterprise Token Store (`OAuthPluginVault`) — they don't land in any vendor MCP intermediary.

## Dual-route MCP server (read + write)

Each plugin's MCP server exposes two routes off the same backend, so the same code can power both a Cowork plugin and (optionally) a tenant-wide [custom federated connector](https://learn.microsoft.com/en-us/microsoft-365/copilot/connectors/set-up-custom-federated-connectors):

| Route | `tools/list` returns | Used by |
|---|---|---|
| `/mcp/full` | Read **and** write tools, with `destructiveHint`/`title` annotations for confirmations | Cowork plugin (`agentConnectors[]`) |
| `/mcp/federated` | Read-only subset only (`search`, `fetch`, `query` shape) | M365 admin center custom federated connector (read-only by spec) |

Plugins ship with the Cowork registration wired up by default. The federated connector is an optional second registration in the M365 admin center; the MCP server already serves the right tool subset on `/mcp/federated`.

## Validation and packaging

Every plugin folder includes a `package.ps1` that:

1. Validates the structure against Cowork rules (`ASKILL-M*`, `ASKILL-P*`, `ASKILL-C*`).
2. Verifies `SKILL.md` frontmatter, kebab-case names, folder/name match.
3. Produces a `.zip` ready for **M365 Admin Center → Manage Apps → Upload custom app**.

```powershell
cd "Cowork Plugins/Slack"
.\package.ps1                # full validation
.\package.ps1 -SkipIcons     # during development
```

## Related

- [Cowork plugin development docs](https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/cowork-plugin-development)
- [Custom federated connectors docs](https://learn.microsoft.com/en-us/microsoft-365/copilot/connectors/set-up-custom-federated-connectors)
- [Cowork Plugin Template/](../Cowork%20Plugin%20Template/readme.md) — generic starting point used to seed plugins in this folder
