---
name: search-and-explore
description: |
  Finds and explores records in {{Service Name}}. Use when the user asks to
  "look up", "find", "search for", "show me", "list", "check the status of",
  or "what do we have in" {{Service Name}}.
metadata:
  author: "{{Your Name}}"
  version: "1.0"
  pattern: discovery
---

# Search and Explore {{Service Name}}

## What This Skill Does

Helps users find, filter, and understand records from {{Service Name}} without
needing to know the API, field names, or query syntax. Translates natural
language requests into structured searches and returns clear, actionable summaries.

## When to Activate

- User asks to find, search, or look up records
- User wants to check the status of something
- User asks "what do we have" or "show me" a category of data
- User provides a name, ID, or keyword and expects matching results

## Workflow

1. **Clarify the search intent.** Determine what entity the user wants
   (e.g., customers, tickets, orders, projects) and any filters (status,
   date range, assignee, priority).

2. **Execute the search.** Use the `search_{{entity}}` tool with the
   appropriate filters. If the user's request is ambiguous, search broadly
   and refine.

   > Example tool call: `search_{{entity}}(query: "acme", status: "active")`

3. **Summarize the results.** Present findings in a scannable format.
   Lead with the count and most relevant matches.

4. **Offer next steps.** Based on what was found, suggest actions:
   - "Want me to update any of these?"
   - "Should I pull the details on [specific record]?"
   - "I can generate a summary report of these results."

## Output Format

Present search results as a table when there are multiple matches:

| Name | Status | Last Updated | Key Detail |
|------|--------|-------------|------------|
| Acme Corp | Active | 2026-04-28 | 3 open tickets |
| Acme Labs | Inactive | 2026-01-15 | Archived |

For a single record, use a structured summary:

**Acme Corp** (ID: ACM-001)
- **Status:** Active
- **Owner:** Jamie Chen
- **Created:** 2025-11-03
- **Open items:** 3 tickets, 1 pending approval

## Handling Edge Cases

- **No results:** Tell the user clearly, suggest broadening the search
  or checking spelling. Offer to search related entities instead.
- **Too many results:** Show the first 10 with a count of total matches.
  Ask the user to narrow down by status, date, or category.
- **Ambiguous entity:** If the user says "find acme" and the API has
  both customers and vendors, ask which one — don't guess.

## Handling Authentication

If a tool call fails because the user hasn't connected to {{Service Name}} yet:

1. Tell the user: "I need to connect to {{Service Name}} to search for that.
   You should see a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm
   they've signed in.
3. If the user has already connected but gets an auth error, suggest:
   "Your {{Service Name}} session may have expired. Try disconnecting and
   reconnecting from Sources & Skills."

## Additional Resources

- **`references/api-field-reference.md`** — Complete field definitions,
  filterable fields, and valid status values for each entity
