# Decision Duck for Copilot Cowork

Decision-support skills + a remote MCP connector for Microsoft 365 Copilot Cowork. Reuses the existing Decision Duck MCP server (Azure Container Apps), protected by Microsoft Entra SSO via the Cowork Plugin Vault.

## What's in the box

Nine skills, one MCP server, one plugin.

### Analysis skills (one per MCP tool)

| Skill | MCP tool | Trigger phrases |
|------|---------|----------------|
| `second-opinion` | `get_second_opinion` | "second opinion", "challenge this", "stress test my thinking" |
| `risk-analysis` | `analyze_risk` | "what could go wrong", "risks", "risk register", "mitigations" |
| `bias-check` | `identify_cognitive_biases` | "bias check", "what biases am I missing", "review my reasoning" |
| `compare-options` | `comparative_analysis` | "compare options", "tradeoffs", "which should we pick" |

### Knowledge skill

| Skill | MCP tools | Trigger phrases |
|------|----------|----------------|
| `decision-frameworks` | `list_frameworks`, `get_framework` | "which framework", "RICE", "OODA", "one-way door", "use a framework" |

### Composite workflow skills (multi-tool orchestration)

| Skill | Calls | Use when |
|------|------|---------|
| `decide` | `comparative_analysis` → `analyze_risk` → `identify_cognitive_biases` | "help me decide", "I need to make a call on" |
| `pre-mortem` | `pre_mortem` | "pre-mortem", "imagine this failed" |
| `red-team` | `red_team` | "red team this", "tear this apart", "be the skeptic" |
| `stakeholder-lens` | `stakeholder_analysis` | "how would [role] see this", "from finance's perspective" |

## Architecture

```
Cowork user
   │
   ▼
Cowork plugin (skills + connector)
   │  OAuth: OAuthPluginVault (referenceId)
   ▼
Entra ID  ──── Bearer token (aud = Decision Duck API)
   │
   ▼
Decision Duck MCP server  (Azure Container Apps)
   │  POST /mcp  (JSON-RPC 2.0)
   │  - tools/list, tools/call
   │  - resources/list, resources/read
   ▼
Azure AI Foundry (gpt-4o-mini / configurable)
```

## Prerequisites

- Frontier preview access for Cowork
- M365 Admin Center access to upload custom agents
- The Decision Duck MCP server already deployed
- A single-tenant Entra app registered in the Cowork tenant
- A **Microsoft Entra SSO client ID registration** in Teams Developer Portal

## Auth: Microsoft Entra SSO (not OAuth client)

The plugin uses the **SSO registration** path, not the OAuth client registration path. The OAuth client path has a known Frontier bug where the popup never closes (postMessage to Teams JS SDK fails). The SSO path bypasses this by using Microsoft's enterprise token store instead of the popup-based redirect.

See `auth/README.md` for full Entra app setup and `server/auth-middleware.md` for the server-side token validation patch.

## Quick start

1. **Add icons.** Place `color.png` (192x192) and `outline.png` (32x32) in the plugin root.

2. **Register SSO.** Follow the Auth section in `auth/README.md`.

3. **Add token validation to the MCP server.** Apply the patch in `server/auth-middleware.md`.

4. **Package.**

   ```powershell
   .\package.ps1                 # full validation
   .\package.ps1 -SkipIcons      # while iterating
   ```

5. **Sideload.** M365 Admin Center → **Agents** → **All Agents** → **Add Agent** → upload the zip.

6. **Connect in Cowork.** Open Cowork → **Sources & Skills** → enable Decision Duck → complete the one-time OAuth consent.

## Plugin rotation

When redeploying after changing auth, rotate both the plugin `id` (new GUID) and append a letter suffix to names. Cowork blocks reusing an already-installed plugin id.

## Files

```
Decision Duck/
├── manifest.json
├── color.png
├── outline.png
├── package.ps1
├── readme.md
├── auth/
│   └── README.md
├── server/
│   └── auth-middleware.md
└── skills/
    ├── second-opinion/SKILL.md
    ├── risk-analysis/SKILL.md
    ├── bias-check/SKILL.md
    ├── compare-options/SKILL.md
    ├── decision-frameworks/SKILL.md
    ├── decide/SKILL.md
    ├── pre-mortem/SKILL.md
    ├── red-team/SKILL.md
    └── stakeholder-lens/SKILL.md
```

## Related

- Decision Duck MCP server source: `../../Decision Duck/`
- Cowork Plugin Template: `../../Cowork Plugin Template/`
