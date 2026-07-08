---
name: discover-skills
description: |
  Discovers and browses agentskills.io skills from GitHub repositories. Use when
  the user asks to "find skills for", "what skills are available", "browse skills",
  "show me .NET skills", "find a skill about", "search for skills", or wants to
  explore what the agentskills.io ecosystem offers.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
---

# Discover Agent Skills

## What This Skill Does

Searches and browses the agentskills.io ecosystem — 8,800+ repositories
containing structured skill files for AI agents. Finds relevant skills by
keyword, lists available skills in known repositories, and retrieves skill
metadata for quick triage.

## When to Activate

- User asks "what skills are available for [topic]"
- User asks to find, search, or browse skills
- User mentions agentskills.io, SKILL.md, or agent skills
- User wants to explore .NET, API design, testing, or other technical topics
- User asks "is there a skill for [task]"

## Workflow

1. **Start with the curated registry.** Use `get_curated_registry` to show
   the user what vetted skill repositories are available, organized by category.

2. **Search if the user has a specific topic.** Use `search_skills` with
   their keywords. This searches across all of GitHub for SKILL.md files.

   > Example: `search_skills(query: "database schema design best practices")`

3. **List skills in a specific repository.** If the user picks a repo from
   the registry or search results, use `list_skills` to enumerate what's
   available.

   > Example: `list_skills(owner: "dotnet", repo: "skills", skills_path: "plugins/dotnet-ai/skills")`

4. **Present results clearly.** Show skill names, their source repository,
   and relevance to the user's question. Offer to load the full skill content.

## Output Format

### For registry browsing

**Available Skill Sources**

| Category | Repository | What's Inside |
|----------|-----------|---------------|
| Architecture & AI | `dotnet/skills` | .NET AI technology selection, RAG patterns, LLM integration |
| Business & Communication | `softaworks/agent-toolkit` | Professional communication, requirements clarity |
| Connector & API Design | `mcp-use/mcp-use` | OpenAPI to MCP conversion |
| Workflow Patterns | `Forward-Future/loopy` | Agent loop patterns, orchestration |

### For search results

Found **12** skills matching "database migration":

| Skill | Repository | Relevance |
|-------|-----------|-----------|
| `ef-core-migration` | dotnet/skills | EF Core migration strategies |
| `database-schema-designer` | softaworks/agent-toolkit | Schema design patterns |

Want me to load any of these for detailed guidance?

## Next Steps After Discovery

- "Load that skill" → hand off to **learn-from-skill**
- "Run that skill" / "Apply it" → hand off to **apply-skill**
- "Search for something else" → refine search
