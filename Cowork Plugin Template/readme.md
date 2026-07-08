# Cowork Plugin Template

Template for building [Microsoft 365 Copilot Cowork](https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/) plugins that connect enterprise API services. Provides three business-workflow skill archetypes, manifest templates, authentication configuration, MCP server guidance, and a validation/packaging script.

## Overview

Cowork plugins extend what Copilot Cowork can do by adding skills (prompt-based workflows) and connectors (remote MCP servers). This template targets the gap between Microsoft's 4 first-party plugins and Claude Code's 100+ developer-tool plugins: **enterprise and vertical SaaS APIs** — ServiceNow, SAP, Workday, industry-specific platforms, and internal company APIs. Skills use the [Agent Skills open standard](https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/cowork-plugin-development#cross-platform-compatibility) and work across Cowork, Claude Code, VS Code Copilot, Gemini CLI, JetBrains Junie, and Cursor.

## Prerequisites

- [Frontier preview program](https://adoption.microsoft.com/en-us/copilot/frontier-program/) access for Cowork
- M365 Admin access to sideload apps (for testing)
- A hosted MCP server if using the connector pattern (Azure Container Apps, Azure Functions, or any HTTPS endpoint supporting JSON-RPC 2.0)

## Example: Microsoft Learn Research

A complete, deployable plugin in [`examples/microsoft-learn/`](examples/microsoft-learn/) that wraps the official [Microsoft Learn MCP Server](https://learn.microsoft.com/en-us/training/support/mcp) with three skills — research docs, compare services, and create learning plans. Zero auth, zero cost, immediately deployable. See [the example readme](examples/microsoft-learn/readme.md) for details.

## Quick Start

### 1. Copy the template

```powershell
Copy-Item -Recurse "Cowork Plugin Template" "My Service Plugin"
```

### 2. Replace placeholders

Search for `{{` across all files and replace with your values:

| Placeholder | Replace with |
|-------------|-------------|
| `{{GUID}}` | A unique GUID (`[guid]::NewGuid()` in PowerShell) |
| `{{company}}` | Your company identifier (lowercase, no spaces) |
| `{{Company Name}}` | Your company display name |
| `{{Service Name}}` | The API/service name (e.g., "ServiceNow", "Workday") |
| `{{Your Name}}` | Skill author name |
| `{{your-mcp-endpoint}}` | Your hosted MCP server URL |
| `{{service-name}}` | Kebab-case service identifier |
| `{{entity}}` | Your primary entity name (e.g., "tickets", "customers") |

### 3. Choose your packaging pattern

| Pattern | Manifest file | When to use |
|---------|--------------|-------------|
| **Skills + Connector** | `manifest.json` | Your API needs live data access via a remote MCP server |
| **Skills Only** | [`manifest-skills-only.json`](manifest-skills-only.json) | Skills guide Cowork's built-in capabilities (Graph, email, files) — no external API |
| **Connector Only** | Remove `agentSkills` from `manifest.json` | MCP tools are self-describing enough for Cowork's built-in skills |

For skills-only, rename `manifest-skills-only.json` to `manifest.json`.

### 4. Customize skills

- Edit each SKILL.md to reference your actual MCP tool names
- Update `api-field-reference.md` with your real entities, fields, and valid values
- Adjust trigger phrases in descriptions to match your domain language
- Add or remove skills as needed for your API's workflow domains

### 5. Add icons

| File | Size | Purpose |
|------|------|---------|
| `color.png` | 192×192 px | Full-color icon for store and app list |
| `outline.png` | 32×32 px | Single-color outline for compact views |

### 6. Validate and package

```powershell
.\package.ps1                # full validation (requires icons)
.\package.ps1 -SkipIcons     # during development
.\package.ps1 -Json          # structured output for CI/CD
```

Or use the M365 Agents Toolkit CLI:

```powershell
atk package --manifest-file ./manifest.json --output-package-file ./plugin.zip --output-folder ./build
```

### 7. Sideload for testing

**Option A: M365 Agents Toolkit CLI (recommended)**

```powershell
npm install -g @microsoft/m365agentstoolkit-cli
atk auth login
atk install --file-path ".\my-plugin.zip" --scope Personal
```

Save the returned `TitleId` and `AppId` for updates and uninstalls.

**Option B: M365 Admin Center**

1. Open **M365 Admin Center** > **Manage Apps** > **Upload custom app**
2. Upload the generated `.zip` file
3. Open **Cowork** > **Sources & Skills** — your skills should appear

## Template Structure

```
Cowork Plugin Template/
├── manifest.json                              # M365 Unified App Manifest (skills + connector)
├── manifest-skills-only.json                  # Alternate manifest (skills only, no connector)
├── color.png                                  # 192×192 full-color icon (you provide)
├── outline.png                                # 32×32 outline icon (you provide)
├── DEPLOYMENT.md                              # ALM and production deployment guide
├── .github/
│   └── workflows/
│       └── validate-plugin.yml                # CI/CD validation and packaging
├── auth/                                      # Auth configuration examples
│   ├── README.md                              # Auth type guide and registration instructions
│   ├── oauth-connector.json                   # OAuthPluginVault connector snippet
│   ├── apikey-connector.json                  # ApiKeyPluginVault connector snippet
│   ├── dcr-connector.json                     # Dynamic Client Registration connector snippet
│   └── none-connector.json                    # No-auth connector snippet
├── server/                                    # MCP server design guidance
│   ├── mcp-server-guide.md                    # Protocol, tool design, timeouts, hosting
│   └── example-tools-list.json                # Example tools/list response to adapt
├── skills/
│   ├── search-and-explore/                    # Discovery workflow pattern
│   │   ├── SKILL.md
│   │   └── references/
│   │       └── api-field-reference.md         # Entity definitions, valid values, query syntax
│   ├── create-and-update/                     # Mutation workflow pattern
│   │   └── SKILL.md
│   ├── report-and-summarize/                  # Aggregation workflow pattern
│   │   └── SKILL.md
│   └── improve-skills/                        # Feedback and iteration pattern
│       └── SKILL.md
├── package.ps1                                # Validation and packaging script
└── readme.md                                  # This file
```

## Skill Archetypes

The three included patterns cover ~80% of what business users ask an API-backed agent to do:

| Skill | Pattern | Trigger phrases |
|-------|---------|-----------------|
| **search-and-explore** | Discovery | "Find", "look up", "show me", "check status" |
| **create-and-update** | Mutation | "Create", "add", "update", "change", "close" |
| **report-and-summarize** | Aggregation | "Summarize", "report on", "how are we doing", "weekly update" |
| **improve-skills** | Feedback | "That wasn't right", "you should have known", "review skill feedback" |
Each SKILL.md includes:
- Trigger phrase examples in the `description` frontmatter
- Numbered workflow steps referencing specific MCP tools
- Output format templates (tables, summaries, confirmations)
- Edge case handling (no results, permission errors, validation failures)
- Authentication handling (what to do when the user hasn't connected yet)

The `improve-skills` skill adds a feedback loop: it records activation gaps and user corrections to the MCP server at runtime, then surfaces accumulated insights when the plugin author prepares the next version. See `skills/search-and-explore/references/skill-improvement.md` for the full iteration guide.

### Adding more skills

Add skills when your API has **distinct workflow domains**. For example, a CRM API might add:
- `qualify-leads` — search + score + recommend
- `prepare-meeting` — cross-entity: pull contact history + recent deals + open tickets

Maximum 20 skills per plugin (10 connectors max). Keep each SKILL.md under 2,000 words and move detailed reference material to `references/` subdirectories.

### Avoid built-in skill name conflicts

Cowork has 13 built-in skills. If a plugin skill has the **same name** as a built-in, the built-in takes priority and your skill is **silently skipped** — no error, no warning. Avoid these names:

`word`, `excel`, `powerpoint`, `pdf`, `email`, `scheduling`, `calendar-management`, `meetings`, `daily-briefing`, `enterprise-search`, `deep-research`, `communications`, `adaptive-cards`

### Writing effective skills

**Write for delegation, not developers.** Skills should read like instructions to a capable assistant:

```markdown
# Good — business workflow
1. Ask the user which time period they want (default to this week)
2. Pull all open tickets using the `search_tickets` tool
3. Group by priority and assignee
4. Highlight anything past SLA
5. Present a summary table and recommend actions

# Bad — implementation details
1. Call GET /api/v1/tickets?status=open&created_after={date}
2. Parse the response JSON array
3. Map the priority field to display values
```

**Make trigger phrases specific.** The `description` field determines when Cowork activates your skill:

```yaml
# Good
description: |
  Finds support tickets in ServiceNow. Use when the user asks to
  "look up a ticket", "find my open incidents", or "check the status of INC-1234".

# Bad
description: Provides ServiceNow ticket access.
```

## Authentication

### Auth types

| Type | Use when | User experience |
|------|----------|----------------|
| `None` | Public APIs, internal services | No prompt |
| `OAuthPluginVault` | OAuth 2.0 APIs (recommended) | One-time consent flow |
| `ApiKeyPluginVault` | API key services | User provides key once |
| Dynamic Client Registration | MCP server supports RFC 7591 DCR | OAuth consent (auto-configured) |

### How it works

1. **Secrets never in the manifest.** The `referenceId` points to credentials in the Microsoft Enterprise Token Store.
2. **Registered through Partner Center.** You provide OAuth client ID, secret, URLs, and scopes during App Store submission. Partner Center generates the `referenceId`.
3. **User-initiated.** Each user completes a one-time sign-in. Admins cannot sign in on behalf of users.
4. **Persistent.** Cowork remembers authorization across conversations until revoked.

### Configuration

The `auth/` folder contains ready-to-use `agentConnectors` snippets. Copy the appropriate one into your `manifest.json`. See [`auth/README.md`](auth/README.md) for the full guide including Dynamic Client Registration (DCR) setup.

For `OAuthPluginVault` and `ApiKeyPluginVault`, the `referenceId` is the OAuth
client registration ID you create when you
[register an OAuth client with Agents Toolkit](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api-plugin-authentication#register-an-oauth-client-with-agents-toolkit).
Set usage by organization to **Any Microsoft 365 Organization** for cross-tenant support.

### Auth handling in skills

Every skill that calls connector tools **must** handle the "not yet connected" state:

1. Tell the user they need to sign in and that a prompt should appear
2. Do NOT retry the tool call until the user confirms
3. Never silently retry mutation operations (create, update, delete)

Each skill template includes a `## Handling Authentication` section.

## MCP Server

If your plugin includes a connector, you need a remote MCP server. The `server/` folder provides:

- **[mcp-server-guide.md](server/mcp-server-guide.md)** — JSON-RPC 2.0 protocol, tool design patterns, response format, async job patterns, auth passthrough, and hosting options
- **[example-tools-list.json](server/example-tools-list.json)** — Example `tools/list` response with full input schemas

### Key constraints

| Constraint | Detail |
|------------|--------|
| **30-second timeout** | Every tool call must complete within 30 seconds. Use pagination, pre-aggregation, or async job patterns |
| **Structured JSON responses** | Return JSON objects with `total_count` and `has_more`, not prose |
| **Parameter descriptions** | Every input parameter needs a `description` — this is how the agent decides what to pass |
| **Tool annotations** | Every tool should have `annotations` (`readOnlyHint`/`destructiveHint`) — tools without them require user confirmation |
| **Error format** | Use `isError: true` for business errors, JSON-RPC error codes only for protocol failures |
| **Auth scoping** | Validate the user's OAuth token and scope data to that user on every request |

## Deployment and ALM

- **[DEPLOYMENT.md](DEPLOYMENT.md)** — End-to-end guide: version management, environment promotion, sideload testing, Partner Center submission, admin deployment, monitoring, updates, and rollback
- **[.github/workflows/validate-plugin.yml](.github/workflows/validate-plugin.yml)** — GitHub Actions CI/CD validation and packaging

### CI/CD output

```powershell
.\package.ps1 -SkipIcons -Json | ConvertFrom-Json
```

```json
{
    "valid": true,
    "errors": [],
    "warnings": ["'search-and-explore/SKILL.md' contains template placeholders"],
    "skillCount": 3,
    "connectorCount": 1,
    "version": "1.0.0",
    "outputPath": "./plugin.zip",
    "sizeKB": 12.4
}
```

### Version management

Keep the `id` (GUID) stable across versions — changing it creates a new plugin. Use semantic versioning: patch for typo fixes, minor for new skills, major for breaking tool schema changes.

## Validation

The `package.ps1` script checks all [Cowork validation rules](https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/cowork-plugin-development#validation-rules):

- **ASKILL-M001–M003:** Manifest-level skill entry validation
- **ASKILL-P001–P008:** Package-level skill structure and frontmatter
- **Connector validation:** Required fields, unique IDs, HTTPS URLs
- **Companion file limits:** Max 20 files, 5MB each, 10MB total per skill

## Cross-Platform Compatibility

| Platform | Compatibility |
|----------|--------------|
| Microsoft 365 Copilot Cowork | Full |
| Claude Code | Full |
| VS Code / GitHub Copilot | Full |
| Gemini CLI | Full |
| JetBrains Junie | Full |
| Cursor | Full |

To maintain a dual Claude Code + Cowork plugin, use the Claude plugin structure as the superset and convert with:

```powershell
.\Convert-ClaudePluginToMOS3.ps1 -PluginPath ./my-plugin -OutputPath ./output
```
