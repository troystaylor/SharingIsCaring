# Microsoft Learn Research — Cowork Plugin Example

A complete, deployable Cowork plugin that wraps the official [Microsoft Learn MCP Server](https://learn.microsoft.com/en-us/training/support/mcp) with business-user-friendly skills for documentation research, service comparison, and learning plan creation.

## Why This Example

This plugin demonstrates the Cowork Plugin Template with a real, public MCP server:

- **Zero auth** — Microsoft Learn MCP requires no authentication
- **Zero cost** — free to use, no API keys or accounts
- **Immediately deployable** — sideload and test in minutes
- **Shows the connector pattern** — skills reference `microsoft_docs_search`, `microsoft_docs_fetch`, and `microsoft_code_sample_search` tools by name

## Skills

| Skill | Pattern | What it does |
|-------|---------|-------------|
| **research-docs** | Discovery | "Find docs about Bicep modules", "how do I configure managed identity?" |
| **compare-services** | Aggregation | "Compare Azure Functions vs Container Apps", "which compute service should I use?" |
| **learning-plan** | Mutation | "Create a learning plan for Azure networking", "help me prepare for AZ-700" |

## MCP Server Details

| Property | Value |
|----------|-------|
| **Endpoint** | `https://learn.microsoft.com/api/mcp` |
| **Transport** | Streamable HTTP |
| **Auth** | None |
| **Tools** | `microsoft_docs_search`, `microsoft_docs_fetch`, `microsoft_code_sample_search` |
| **Token budget** | Append `?maxTokenBudget=2000` to cap search response sizes |
| **Docs** | [Developer reference](https://learn.microsoft.com/en-us/training/support/mcp-developer-reference) |
| **Best practices** | [Best practices guide](https://learn.microsoft.com/en-us/training/support/mcp-best-practices) |

## Deploy

### 1. Add icons

Create `color.png` (192×192) and `outline.png` (32×32) in this folder.

### 2. Package

```powershell
# From the template root
.\package.ps1 -Path .\examples\microsoft-learn
```

### 3. Sideload

**Option A: M365 Agents Toolkit CLI**

```bash
npm install -g @microsoft/m365agentstoolkit-cli
atk auth login
atk install --file-path "./microsoft-learn.zip" --scope Personal
```

**Option B: M365 Admin Center**

1. **M365 Admin Center** → **Manage Apps** → **Upload custom app**
2. Upload the generated `.zip`
3. Open **Cowork** → **Sources & Skills** — three skills should appear

### 4. Test

Try these prompts in Cowork:

- "Find the latest docs on Copilot Cowork plugin development"
- "Compare Azure Functions and Azure Container Apps for running an MCP server"
- "Create a learning plan for Power Platform custom connectors"
- "What's new in Microsoft Foundry?"
- "Find C# code samples for Azure AI Search"

## How This Maps to the Template

| Template archetype | This example |
|-------------------|-------------|
| `search-and-explore` | `research-docs` — search and fetch documentation |
| `report-and-summarize` | `compare-services` — aggregates info from multiple searches |
| `create-and-update` | `learning-plan` — creates a structured output document |
| `improve-skills` | Not included (add from template if deploying to production) |

## Structure

```
examples/microsoft-learn/
├── manifest.json
├── skills/
│   ├── research-docs/
│   │   └── SKILL.md
│   ├── compare-services/
│   │   └── SKILL.md
│   └── learning-plan/
│       └── SKILL.md
└── readme.md
```

## Notes

- The Microsoft Learn MCP Server [refreshes daily](https://learn.microsoft.com/en-us/training/support/mcp#limitations). Results reflect publicly available docs — not training modules, learning paths, or exam content.
- Tool names and schemas may change. The [best practices guide](https://learn.microsoft.com/en-us/training/support/mcp-best-practices) recommends calling `tools/list` at runtime rather than hardcoding tool names.
- This example doesn't include the `improve-skills` feedback skill to keep it minimal. Copy it from the template's `skills/improve-skills/` folder if deploying to production.
