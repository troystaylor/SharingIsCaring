# .NET Skills

## Overview

Dual-mode Power Platform connector for the [dotnet/skills](https://github.com/dotnet/skills) repository — Microsoft's curated set of .NET development guidance for AI coding agents.

**MCP mode** — Copilot Studio agents query .NET best practices, code patterns, and development workflows directly from the official skill repository.

**Typed operations** — Power Automate flows can list plugins, retrieve skill content, and search for .NET guidance by keyword with full IntelliSense.

## Available Plugins

| Plugin | Description |
|--------|-------------|
| dotnet | C# language server (LSP) integration and high-level .NET development skills |
| dotnet-advanced | Niche .NET skills for special scenarios |
| dotnet-ai | AI/ML skills: technology selection, LLM integration, agentic workflows, RAG, MCP |
| dotnet-aspnetcore | ASP.NET Core web development: middleware, endpoints, APIs |
| dotnet-blazor | Blazor development: component authoring, interactivity |
| dotnet-data | Data access and Entity Framework skills |
| dotnet-diag | Performance investigations, debugging, incident analysis |
| dotnet-maui | .NET MAUI development: environment setup, diagnostics |
| dotnet-msbuild | MSBuild: failure diagnosis, performance, code quality, modernization |
| dotnet-nuget | NuGet package management and modernization |
| dotnet-template-engine | Template discovery, project scaffolding, template authoring |
| dotnet-test | Test execution, generation, analysis, coverage, MSTest workflows |
| dotnet-test-migration | Test framework migrations: MSTest/xUnit upgrades and conversions |
| dotnet-upgrade | Migrating and upgrading .NET projects across framework versions |
| dotnet11 | New .NET 11 APIs and language features |

## MCP Tools

| Tool | Description |
|------|-------------|
| `list_plugins` | List all available .NET skill plugins |
| `list_skills` | List skills within a specific plugin |
| `get_skill` | Get full SKILL.md content with workflows and code examples |
| `get_plugin_details` | Get plugin.json manifest with metadata |
| `search_skills` | Search across all skills by keyword |

## Typed Operations

| Operation | Method | Description |
|-----------|--------|-------------|
| List Plugins | GET | List all plugin directories |
| Get Plugin Details | GET | Retrieve plugin.json manifest |
| List Skills | GET | List skills within a plugin |
| Get Skill Content | GET | Retrieve full SKILL.md (decoded) |
| Search Skills | GET | Search across all skill files |

## Prerequisites

- GitHub Personal Access Token (PAT) with public repo read access
  - Fine-grained token scoped to `dotnet/skills` with Contents read permission is sufficient
  - Classic token with `public_repo` scope also works
- Connection parameter format: `token ghp_yourPAThere`

## Setup

1. Import the connector to your Power Platform environment
2. Create a connection using your GitHub PAT
3. For Copilot Studio: add the connector as an action and enable the MCP endpoint
4. For Power Automate: use the typed operations with dynamic dropdowns

## Authentication

Uses API key authentication via the `Authorization` header. The token is passed directly to the GitHub API for content access. Since `dotnet/skills` is a public repository, even unauthenticated requests work (60 req/hr), but a PAT provides 5,000 req/hr.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Copilot Studio Agent                                    │
│  (MCP: tools/call → list_plugins, get_skill, etc.)      │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────┐
│  .NET Skills Connector (script.csx)                      │
│  ┌─────────────┐  ┌──────────────────────────────────┐  │
│  │ MCP Handler │  │ Typed Operations (Power Automate) │  │
│  │ /mcp        │  │ /repos/dotnet/skills/contents/... │  │
│  └──────┬──────┘  └──────────────┬───────────────────┘  │
│         │                        │                       │
│         └────────────┬───────────┘                       │
│                      │                                   │
└──────────────────────┼───────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────┐
│  GitHub API (api.github.com)                             │
│  - Contents API: /repos/dotnet/skills/contents/...       │
│  - Search API: /search/code                              │
└─────────────────────────────────────────────────────────┘
```

## Example Use Cases

### Copilot Studio Agent
- "What .NET AI technologies should I use for document classification?"
- "Show me MSBuild performance optimization guidance"
- "How do I migrate from xUnit to MSTest?"

### Power Automate
- Scheduled flow: check for new skills added to the repo weekly
- When a dev asks a question in Teams → search skills → return guidance
- Build a knowledge base by fetching all skill content and indexing it
