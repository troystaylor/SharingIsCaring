# Power Compendium

An organizational knowledge base connector for Power Platform and M365 Copilot. Inspired by [Karpathy's LLM Wiki pattern](https://gist.github.com/karpathy/442a6bf555914893e9891c11519de94f) — instead of re-deriving knowledge from raw documents on every query (like RAG), the LLM incrementally builds and maintains a persistent compendium of interlinked pages.

## Overview

11 operations accessible via MCP (Copilot Studio, M365 Copilot, VS Code) and REST (Power Automate, Power Apps):

### Book Operations
| Operation | Description |
|-----------|-------------|
| **Ingest Source** | Process a source document → LLM extracts entities/concepts → creates/updates pages → maintains cross-references |
| **Ingest Skill** | Process a multi-file agent skill → all files analyzed together → extracted as interlinked pages |
| **Ingest from URL** | Fetch a skill or document from a URL (GitHub repos, direct files) → auto-ingest |
| **Query Book** | Search relevant pages → synthesize answer with citations → optionally save as new page |
| **Lint Book** | Health-check for contradictions, stale claims, orphan pages, missing cross-references, knowledge gaps |

### Page Management
| Operation | Description |
|-----------|-------------|
| **List Pages** | List all pages with metadata (title, category, scope, last updated, source count) |
| **Read Page** | Read full content and metadata of a specific page |
| **Write Page** | Create or update a page with markdown content |
| **Delete Page** | Soft-delete a page (recoverable) |
| **Promote Page** | Copy a page from your personal book to the shared org book |

## Ingestion Options

Three ways to feed knowledge into the compendium:

### Option A: Single Source (`ingest_source`)
Ingest a single text document. The LLM reads it, extracts key information, creates or updates relevant pages, and maintains cross-references.

```json
POST /api/book/ingest?scope=org
{
    "title": "Rubberducking with LLMs",
    "content": "Rubber duck debugging is a method where...",
    "sourceUrl": "https://deepgram.com/learn/llms-the-rubber-duck-debugger",
    "category": "article"
}
```

**Best for**: Articles, meeting notes, API documentation, papers, individual files.

### Option B: Multi-File Skill (`ingest_skill`)
Ingest an entire agent skill (multiple files processed together). The LLM understands the relationship between instruction files, templates, and examples — extracting richer cross-references than individual ingestion.

```json
POST /api/book/ingest-skill?scope=org
{
    "skillName": "mcp-connector",
    "files": [
        {"path": "SKILL.md", "content": "# MCP Connector for Copilot Studio\n\nBuild Model Context Protocol..."},
        {"path": "template/script.csx", "content": "public class Script : ScriptBase\n{..."},
        {"path": "template/apiDefinition.swagger.json", "content": "{\"swagger\":\"2.0\",...}"}
    ]
}
```

**Best for**: Agent skills, multi-file templates, code packages with documentation.

### Option C: URL-Based Fetch (`ingest_from_url`)
Point the compendium at a URL and it fetches the content automatically. Supports GitHub repository tree URLs (fetches all markdown, code, JSON, and YAML files) and direct file URLs.

```json
POST /api/book/ingest-from-url?scope=org
{
    "url": "https://github.com/troystaylor/SharingIsCaring/tree/main/.github/skills/mcp-connector",
    "type": "agent-skill"
}
```

GitHub tree URLs are converted to API calls that list and download all text files in the directory. Direct file URLs fetch a single file. Both then process through Option B's multi-file pipeline.

**Best for**: Ingesting skills from GitHub repos, remote documentation, public articles.

### How Ingestion Works

Regardless of which option you use, the LLM processing pipeline is the same:

1. **Read** — The LLM reads the source content and the current book index
2. **Plan** — It determines what pages to create (entities, concepts, source summaries) and what existing pages to update
3. **Write** — New pages are created and existing pages are updated with new information
4. **Cross-reference** — Pages link to each other using `[[page-id]]` syntax
5. **Index** — All new/updated pages are indexed in Azure AI Search
6. **Log** — A chronological entry is appended to the book's activity log

### Scoping
Every operation accepts a `scope` parameter:
- **org** (default) — shared organizational book
- **personal** — your private book (keyed to your Entra ID)
- **all** — search across both (query and list only)

## Architecture

```
M365 Copilot / VS Code / Claude Desktop
    ↓ (MCP, native — via Agents Toolkit or direct)
                                            Azure Container App
Copilot Studio / Power Automate / Power Apps    ↓ POST /api/mcp
    ↓ (MCP via Power Platform connector)    ↓ REST /api/book/*
    Power Compendium Connector ──────────→  ASP.NET Core API
        (script.csx for MCP only)               ├── BookService (ingest/query/lint/CRUD/promote)
                                                ├── BookStorageService (Azure Blob Storage)
                                                ├── BookSearchService (Azure AI Search)
                                                ├── BookLlmService (Azure OpenAI)
                                                └── EasyAuth (Entra ID OAuth v2)
```

### Storage Layout
```
compendium-pages/              ← Azure Blob container
├── org/                       ← Shared organizational book
│   ├── pages/{pageId}.json
│   ├── sources/{sourceId}.json
│   ├── deleted/               ← Soft-deleted pages
│   └── log.md                 ← Chronological activity log
└── users/{oid}/               ← Personal books (per Azure AD object ID)
    ├── pages/
    ├── sources/
    └── log.md
```

## Azure Resources

| Resource | Purpose |
|----------|---------|
| **Azure Container App** | Hosts the ASP.NET Core API (`compendium-api`) |
| **Azure Blob Storage** | Stores pages as JSON, sources, and logs |
| **Azure AI Search** | Indexes pages for query operations (RBAC auth, `aadOrApiKey`) |
| **Azure OpenAI** | LLM processing for ingest, query, and lint (gpt-4.1-mini) |
| **Azure Container Registry** | Stores Docker images |
| **Entra ID App Registration** | OAuth v2 authentication for all consumers |

### Deployed Endpoint
`https://your-compendium-api.azurecontainerapps.io`

## MCP Integration

### M365 Copilot (Native MCP)
Use Agents Toolkit → "Start with an MCP Server" → point to `/api/mcp`. The toolkit auto-discovers 10 tools: `ingest_source`, `ingest_skill`, `ingest_from_url`, `query_book`, `lint_book`, `list_pages`, `read_page`, `write_page`, `delete_page`, `promote_page`.

### Copilot Studio
Deploy the Power Platform connector (`apiDefinition.swagger.json` + `apiProperties.json` + `script.csx`). The `InvokeMCP` operation routes through `script.csx` which handles JSON-RPC 2.0.

### VS Code / Claude Desktop / Other MCP Clients
Connect directly to `https://your-compendium-api.azurecontainerapps.io/api/mcp` with a Bearer token.

## Page Categories

| Category | Purpose | Example |
|----------|---------|---------|
| `entity` | A specific thing (person, product, API, service) | "Azure Container Apps", "Salesforce OAuth" |
| `concept` | An idea, pattern, or principle | "Token refresh patterns", "Rubberducking" |
| `source` | Summary of an ingested source document | "Karpathy LLM Wiki (2026-04)" |
| `comparison` | Side-by-side analysis of alternatives | "Blob Storage vs SharePoint Embedded" |
| `overview` | High-level synthesis across multiple pages | "Authentication patterns overview" |

## Security

- **EasyAuth** validates Entra ID OAuth v2 tokens before requests reach application code
- **Managed Identity** for all Azure service access (Blob, Search, OpenAI) — no keys stored
- **Input validation** — pageId allowlist (kebab-case only), content size limits (100KB), question limits (1KB)
- **Rate limiting** — 30 requests/minute per user on LLM-bound endpoints
- **Log sanitization** — user-provided content stripped of markdown injection before logging

## Deploying the Connector

```bash
pac connector create \
  --api-def connector/apiDefinition.swagger.json \
  --api-prop connector/apiProperties.json \
  --script connector/script.csx
```

When creating a connection, sign in with your Entra ID account. No additional configuration needed — the connector host is set in the swagger definition.

## Deploying Azure Infrastructure

```powershell
.\infra\deploy.ps1 `
    -ResourceGroup rg-power-compendium `
    -OpenAiResourceGroup myResourceGroup `
    -OpenAiAccountName my-openai-resource `
    -OpenAiDeploymentName gpt-4o
```

The deploy script provisions all Azure resources (Storage, AI Search, ACR, Container Apps), builds and pushes the container image, assigns RBAC, and verifies the deployment. See [infra/deploy.ps1](infra/deploy.ps1) for all options including `-SkipInfra` and `-SkipBuild`.

## Files

```
Power Compendium/
├── connector/                    # Power Platform custom connector
│   ├── apiDefinition.swagger.json    # OpenAPI 2.0 (11 REST ops + MCP endpoint)
│   ├── apiProperties.json            # OAuth config (Entra ID v2)
│   └── script.csx                    # MCP handler for Copilot Studio (10 tools)
├── container-app/                # ASP.NET Core API (deployed to Azure Container Apps)
│   ├── Controllers/              # BookController + McpController
│   ├── Services/                 # BookService, BookStorageService, BookSearchService, BookLlmService, McpHandler
│   ├── Models/                   # Request/response DTOs
│   ├── Dockerfile
│   ├── Program.cs
│   └── LLMWiki.Api.csproj
├── infra/                        # Infrastructure as Code
│   ├── main.bicep                # Azure resources (Storage, AI Search, ACR, ACA, RBAC)
│   └── deploy.ps1                # Deployment script (build, deploy, configure)
└── readme.md                     # This file
```
