---
name: jql-cql-builder
description: JQL and CQL query patterns and a configurable reference of your projects, spaces, issue types, statuses and custom fields, so queries are written from known fields and counts stay exact. Use before building any Jira JQL or Confluence CQL query, board or sprint lookup, or count.
---

# JQL and CQL Builder

Build queries from this reference and from tool output. Never guess a project key, space key, status name or custom field ID.

## Organization reference (fill in; unfilled rows must be looked up, not guessed)

| Item | Value |
| --- | --- |
| Support project key(s) | [[CONFIGURE: project keys]] |
| Delivery / engineering project key(s) | [[CONFIGURE]] |
| Knowledge base Confluence space key(s) | [[CONFIGURE]] |
| Engineering guidelines space or page IDs | [[CONFIGURE]] |
| Issue types in use (Bug, Incident, Task, Epic, ...) | [[CONFIGURE]] |
| Status names and the statusCategory they map to | [[CONFIGURE]] |
| Priority names | [[CONFIGURE]] |
| Custom field names and IDs (customer, release, severity, product version, environment) | [[CONFIGURE]] |
| Labels or components that mark known issues, KB candidates | [[CONFIGURE]] |
| Board IDs for weekly reporting | [[CONFIGURE]] |

If a value is unfilled, discover it: `listJiraProjects`, `listJiraProjectIssueTypesMetadata`, `getJiraIssueTypeMetaWithFields` (custom field IDs), `listJiraStatuses`, `listJiraBoards`, `listConfluenceSpaces`. Ask the user before assuming.

## JQL patterns

- Open work: `project = "KEY" AND statusCategory != Done ORDER BY priority DESC, updated DESC`
- Resolved in a window: `project = "KEY" AND resolved >= -7d AND resolved < 0d`
- Created in a window: `project = "KEY" AND created >= startOfWeek(-1) AND created < startOfWeek()`
- Free-text duplicates: `project = "KEY" AND (summary ~ "term" OR text ~ "term") ORDER BY created DESC` (use short key terms, not sentences)
- Error code or message: `text ~ "\"exact phrase\""`
- Sprint: `project = "KEY" AND sprint in openSprints()` (team-managed boards of type simple have no sprints; fall back to a statusCategory query)
- Release: `fixVersion = "name"` (names from `getJiraProjectVersions`)
- Blocked: `statusCategory != Done AND (status = Blocked OR labels in (blocked))` using the status names above
- Unassigned: `assignee is EMPTY`; mine: `assignee = currentUser()`
- Worklogs: `worklogAuthor = currentUser() AND worklogDate >= -7d`
- Stale: `statusCategory != Done AND updated <= -14d`
- Always quote string values, always add `ORDER BY`, and include resolved issues when looking for duplicates or fix history.
- Request only the fields you need (key, summary, status, priority, assignee, created, updated, resolutiondate, plus custom field IDs from the table).

## CQL patterns

- By space and type: `space = "KEY" AND type = page AND text ~ "term" ORDER BY lastmodified DESC`
- Title match: `title ~ "term"` (use `searchConfluence`, not semantic `search`, when you need a title, type, label or date filter)
- Recently changed: `space = "KEY" AND lastmodified >= now("-30d")`
- Labels: `label = "kb-candidate"`
- Under a parent page: `ancestor = 12345`
- Quote values; keep `limit` small (10 to 25) and page.

## Counts and totals

- Report a count only from a total the tool returns or by paging to the end and counting. If the tool returns no total and the set is too large to page, say the count is a lower bound and show the query.
- Show the exact JQL or CQL used next to every number so the user can rerun it.
- Never mix windows. State the start and end of each period and the time zone.

## Checks before running

1. Fields and values exist (see the reference or discovery).
2. The window and scope match what the user asked.
3. The query is bounded (limit and ORDER BY).

Also apply `atlassian-access` and `evidence-and-uncertainty`.
