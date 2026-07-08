# Agent Skills

## Overview

Universal bridge from the [agentskills.io](https://agentskills.io/) open standard to Microsoft Copilot Studio and Power Automate. Makes Copilot Studio a first-class client of the Agent Skills ecosystem — capable of discovering, enumerating, and consuming skills from any compliant repository.

**Why this matters**: The agentskills.io [client showcase](https://agentskills.io/clients) lists 30+ coding agents (VS Code, Claude Code, Cursor, Gemini CLI, etc.) but not Copilot Studio. This connector bridges that gap, giving low-code/no-code agents access to the same curated skill content that coding agents use natively.

## How It Works

The connector implements the agentskills.io [progressive disclosure](https://agentskills.io/specification) pattern via MCP:

```
┌─────────────────────────────────────────────────────────────┐
│  1. DISCOVERY         │  get_curated_registry               │
│  (~0 tokens)          │  discover_repositories              │
├───────────────────────┼─────────────────────────────────────┤
│  2. ENUMERATION       │  list_skills (names only)           │
│  (~100 tokens/skill)  │  get_skill_metadata (frontmatter)   │
├───────────────────────┼─────────────────────────────────────┤
│  3. ACTIVATION        │  get_skill (full SKILL.md body)     │
│  (<5000 tokens)       │  search_skills (cross-repo search)  │
└───────────────────────┴─────────────────────────────────────┘
```

## MCP Tools

| Tool | Progressive Disclosure Stage | Description |
|------|------------------------------|-------------|
| `get_curated_registry` | Discovery | Returns curated list of known skill repositories |
| `discover_repositories` | Discovery | Searches GitHub for repos with `agent-skills` topic |
| `list_skills` | Enumeration | Lists skill directories in a repo/path |
| `get_skill_metadata` | Enumeration | Extracts YAML frontmatter only (name, description) |
| `get_skill` | Activation | Loads full SKILL.md with instructions and examples |
| `search_skills` | Activation | Cross-repo search for SKILL.md files by keyword |

## Curated Registry

The connector includes a built-in registry of known skill repositories:

| Repository | Description |
|-----------|-------------|
| `dotnet/skills` | .NET development: C#, MSBuild, NuGet, AI/ML, ASP.NET Core, Blazor, MAUI, testing |
| `anthropics/skills` | Anthropic's reference skills and templates |
| `github/awesome-copilot` | Community skills, agents, instructions, and prompts |
| `laravel/boost` | Laravel development best practices |

## Typed Operations (Power Automate)

| Operation | Description |
|-----------|-------------|
| Discover Skill Repositories | Search GitHub for repos with `agent-skills` topic |
| List Skills | List skill directories at a given path in any repo |
| Get Skill | Retrieve decoded SKILL.md content |
| Search Skills | Cross-repo search for SKILL.md files |
| Get Repository Topics | Verify a repo has the `agent-skills` topic |

## Enumeration Strategy

Three complementary discovery mechanisms:

1. **Curated registry** — Hardcoded known-good repos (dotnet/skills, anthropics/skills, etc.). Fast, reliable, zero API calls.

2. **GitHub Topics** — `topic:agent-skills` is the standard tag. The `discover_repositories` tool searches for this. Community repos self-declare by adding the topic.

3. **Code search** — `filename:SKILL.md` finds skills across all of GitHub regardless of whether the repo is tagged. Combined with keyword queries, this is the broadest discovery net.

## Prerequisites

- GitHub Personal Access Token (PAT) with public repo read access
  - Fine-grained token with Contents read is sufficient
  - Classic token with `public_repo` scope also works
- Connection format: `token ghp_yourPAThere`

## Setup

1. Import the connector to your Power Platform environment
2. Create a connection using your GitHub PAT
3. For Copilot Studio: add as an MCP action — the agent uses progressive disclosure automatically
4. For Power Automate: use typed operations to build skill pipelines

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Copilot Studio Agent (MCP client)                          │
│                                                              │
│  "What .NET AI technology should I use?"                     │
│   → get_curated_registry → list_skills(dotnet-ai)           │
│   → get_skill(technology-selection)                          │
│   → Returns full guidance with code examples                 │
└──────────────────────────┬──────────────────────────────────┘
                           │ MCP (JSON-RPC 2.0)
┌──────────────────────────▼──────────────────────────────────┐
│  Agent Skills Connector (script.csx)                         │
│  Implements agentskills.io progressive disclosure via MCP    │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│  GitHub API                                                  │
│  • Contents API → skill file retrieval                       │
│  • Search API → discovery and cross-repo search              │
│  • Topics API → repository classification                    │
└─────────────────────────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│  agentskills.io Ecosystem                                    │
│  • dotnet/skills (4.3k★)                                     │
│  • anthropics/skills                                         │
│  • laravel/boost                                             │
│  • Any repo with topic:agent-skills + SKILL.md files         │
└─────────────────────────────────────────────────────────────┘
```

## Getting Listed on agentskills.io

This connector makes Copilot Studio a legitimate agentskills.io client because it:

1. **Supports the standard format** — reads SKILL.md files with YAML frontmatter per specification
2. **Implements progressive disclosure** — discovery → enumeration → activation stages
3. **Works with any compliant repo** — not locked to a single source
4. **Respects the spec** — handles name, description, license, compatibility, metadata fields

A PR to [agentskills/agentskills](https://github.com/agentskills/agentskills) could add Copilot Studio to the showcase with:
- Setup instructions pointing to this connector's deployment guide
- Source code at the SharingIsCaring repo
