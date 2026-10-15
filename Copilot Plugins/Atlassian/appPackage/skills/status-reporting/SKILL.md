---
name: status-reporting
description: Build a weekly executive status report from Jira totals, boards, sprints, project versions and Confluence pages, with every number traced to a query. Use when asked for a weekly status, executive summary, program or project update, progress against milestones, or risks and blockers.
---

# Status Reporting

Read-only. All numbers come from tool output. Apply `atlassian-access`, `jql-cql-builder` and `evidence-and-uncertainty` first.

## Inputs to confirm

- Scope (project keys, board, program), the reporting window with start and end dates and time zone (default: last full week), the audience, and the format: **Weekly Status** (one page, structured, weekly) or **Executive Summary** (shorter, decisions first).
- Whether a prior report exists to compare with. If so, find it with `searchConfluence` and use its figures only as cited prior values.

## How to do it

1. **Counts.** Run bounded JQL for each metric over the window and report the exact query and total: created, resolved, open, blocked, overdue, unassigned, high priority open, and aged beyond the stale threshold. Use `jql-cql-builder` for patterns.
2. **Delivery progress.** For sprint or board work use `listJiraBoards`, `getJiraBoardSprintData`, `listJiraBoardSprints` and `getJiraBoardIssueData` through `discover` and `executeRead`. For milestones use `getJiraProjectVersions` (release names, dates, status). Cite the board or version.
3. **Context.** Search Confluence for the program's status or decision pages in the window, read the relevant ones, and cite them.
4. **Highlights and risks.** Choose items from the data: largest completions, items past due, blocked high priority items, items with no assignee, and anything that changed status repeatedly (check `listJiraIssueChangelogs` for the few that matter). State why each is included.
5. **Status rating.** Give a rating only by an explicit rule you show (for example, red if any blocked critical item has been open more than N days; amber if overdue share exceeds N percent). If the rule is not configured [[CONFIGURE: RAG thresholds]], propose one and ask, or omit the rating.
6. **Compose.**
   - **Weekly Status:** headline, rating with rule, metrics table (metric, value, window, query), progress by workstream or release, risks and blockers (owner as recorded, otherwise `Owner unknown`), decisions needed, next week, appendix of queries and sources.
   - **Executive Summary:** headline, decisions needed, three to five highlights, top risks, one small metrics table, sources.
7. **Trends** only when both periods were queried with the same query; show both values.
8. To publish to Confluence, follow `write-safety` (draft only).

## What to watch for

- Do not compute percentages or deltas from remembered numbers; compute only from values returned in this session and show the inputs.
- If a metric could not be queried, show `not available` and why, not a blank or a guess.
- Do not name individuals as performance issues; report work, not people.
