# Skill Iteration Guide

How to improve your Cowork plugin skills over time using real-world usage data.

## The Feedback Loop

```
1. Users interact with skills
         ↓
2. MCP server records feedback (via record_skill_feedback tool)
         ↓
3. Author reviews insights (via get_skill_insights tool or Purview logs)
         ↓
4. Author updates SKILL.md files
         ↓
5. New plugin version deployed
         ↓
(repeat)
```

## Common Skill Problems

| Symptom | Root cause | Fix |
|---------|-----------|-----|
| Skill doesn't activate | Trigger phrases too narrow | Add the user's actual phrasing to `description` |
| Wrong skill activates | Overlapping descriptions | Make each skill's scope more specific, add exclusions |
| Skill activates but produces poor output | Workflow steps don't match real usage | Restructure workflow based on what users actually ask for |
| User has to re-explain context | Skill doesn't gather enough info upfront | Add a clarification step early in the workflow |
| Output format isn't useful | Template doesn't match how users consume data | Change format (table vs. summary vs. list) based on feedback |

## Method 1: Purview Audit Logs (Passive)

Microsoft Purview captures Cowork interactions under **Copilot activities** at no
extra cost (Audit Standard). Use these to identify:

### What to look for

- **Skill activation frequency** — Which skills are used most? Which are never
  activated? An unused skill may have poor trigger phrases or duplicate a built-in.
- **Conversation patterns** — What do users ask before a skill activates? These
  are candidate trigger phrases to add to `description`.
- **Error patterns** — Repeated failures from specific tools suggest schema issues
  or API changes.

### Accessing audit logs

1. Go to **Microsoft Purview compliance portal** → **Audit**
2. Filter by **Copilot activities**
3. Look for entries with your plugin name
4. Export to CSV for analysis

## Method 2: MCP Server Feedback Tools (Active)

Add two tools to your MCP server that let the skill itself capture and retrieve
feedback at runtime. This is more targeted than audit logs because it captures
**why** something didn't work, not just that it happened.

### How it works

1. **During conversations** — When a user corrects Cowork, rephrases a request,
   or the skill produces a poor result, the `improve-skills` skill calls
   `record_skill_feedback` on your MCP server to log what happened.

2. **During development** — The plugin author (or an agent) calls
   `get_skill_insights` to retrieve accumulated feedback, grouped by skill
   and type (missed activation, poor output, suggested trigger phrase).

3. **Between versions** — The author uses insights to update SKILL.md files
   with better trigger phrases, refined workflows, and improved output formats.

### Feedback tool schema

Your MCP server should expose two tools:

#### record_skill_feedback

Records a single feedback event from a conversation.

```json
{
    "name": "record_skill_feedback",
    "description": "Record feedback about a skill's performance. Use when the user corrects output, rephrases a request that should have activated a skill, or explicitly says something didn't work.",
    "inputSchema": {
        "type": "object",
        "properties": {
            "skill_name": {
                "type": "string",
                "description": "The skill this feedback is about (e.g., 'search-and-explore')"
            },
            "feedback_type": {
                "type": "string",
                "enum": ["missed_activation", "wrong_skill", "poor_output", "missing_feature", "trigger_phrase"],
                "description": "What kind of problem occurred"
            },
            "user_input": {
                "type": "string",
                "description": "What the user said or asked (anonymized — no PII)"
            },
            "expected_behavior": {
                "type": "string",
                "description": "What should have happened instead"
            },
            "suggested_trigger": {
                "type": "string",
                "description": "A trigger phrase to add to the skill's description (for trigger_phrase type)"
            }
        },
        "required": ["skill_name", "feedback_type"]
    }
}
```

#### get_skill_insights

Retrieves accumulated feedback for the plugin author.

```json
{
    "name": "get_skill_insights",
    "description": "Retrieve accumulated skill feedback and improvement suggestions. Use during skill development to review what needs updating before the next version.",
    "inputSchema": {
        "type": "object",
        "properties": {
            "skill_name": {
                "type": "string",
                "description": "Filter to a specific skill (optional — omit for all skills)"
            },
            "feedback_type": {
                "type": "string",
                "enum": ["missed_activation", "wrong_skill", "poor_output", "missing_feature", "trigger_phrase"],
                "description": "Filter to a specific feedback type (optional)"
            },
            "since": {
                "type": "string",
                "format": "date",
                "description": "Only return feedback recorded after this date (ISO 8601)"
            },
            "limit": {
                "type": "integer",
                "default": 50,
                "description": "Maximum number of feedback entries to return"
            }
        }
    }
}
```

### Storage considerations

Feedback data should be stored server-side in your MCP server's backing store:

| Option | Good for | Notes |
|--------|----------|-------|
| **Azure Blob Storage** | Simple append-only log | Store as JSONL, cheap, easy to query with tools |
| **Azure Table Storage** | Structured queries by skill/type | PartitionKey = skill_name, RowKey = timestamp |
| **Database (SQL, Cosmos)** | Complex analytics | Overkill for most plugins |
| **In-memory + periodic flush** | Development/testing | Not suitable for production |

### Privacy

- **Never store PII** in feedback entries. The `user_input` field should be
  the request pattern (e.g., "find my open tickets from last week"), not
  the user's identity or actual data.
- **Anonymize before recording.** Strip names, emails, IDs from the user input.
- **Scope feedback to the plugin, not the user.** Don't track which user
  provided feedback — track which patterns need improvement.
- **Retention policy.** Delete feedback older than 90 days unless the author
  explicitly archives it.

## Applying Insights

### Updating trigger phrases

The most common improvement. Add phrases from real usage to the `description`:

```yaml
# Before — missed "show me overdue tickets"
description: |
  Finds support tickets. Use when the user asks to "look up a ticket"
  or "search for incidents".

# After — added observed user phrases
description: |
  Finds support tickets. Use when the user asks to "look up a ticket",
  "search for incidents", "show me overdue tickets",
  "what's assigned to me", or "check the status of INC-1234".
```

### Restructuring workflows

If feedback shows users consistently need a step you don't have:

```markdown
# Before — users kept asking "who else is affected?"
1. Search for the ticket
2. Show ticket details

# After — added related-items step
1. Search for the ticket
2. Show ticket details
3. Search for related tickets (same category, same timeframe)
4. Note if this appears to be part of a larger incident
```

### Version bumping

- Trigger phrase improvements → **patch** version (1.0.0 → 1.0.1)
- Workflow restructuring → **minor** version (1.0.0 → 1.1.0)
- New skill added from feedback → **minor** version
