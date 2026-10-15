---
name: backlog-hygiene
description: Read-only review of a Jira backlog that finds likely duplicates, stale items, missing fields, unassigned work and mismatched types, with the query behind every finding. Use when asked to groom, clean up, audit or health-check a backlog, project or board, or to find duplicate or stale issues.
---

# Backlog Hygiene

Reports and proposes only; it changes nothing. Apply `atlassian-access`, `jql-cql-builder` and `evidence-and-uncertainty` first.

## Inputs to confirm

- Scope: project key, board or filter. Do not guess; ask if missing.
- Thresholds: stale after N days without update (default 90 for backlog, 14 for in-progress), and which fields count as required [[CONFIGURE: required fields]]. Show the thresholds you used.

## How to do it

1. **Pull the scope** with `searchJiraIssuesUsingJql`, for example `project = "KEY" AND statusCategory != Done ORDER BY updated ASC`. Request key, summary, issuetype, status, priority, assignee, reporter, created, updated, labels, components, fixVersion, parent and any required custom fields. Page until complete or a stated cap. Derive most findings locally from this set.
2. **Findings** (each with count, the rule, and a table of keys):
   - **Stale**: not updated within the threshold.
   - **Unassigned** and **no priority**.
   - **Missing required fields**: empty fixVersion, components, parent or the configured fields.
   - **Type mismatch**: for example a Task whose summary reads like a defect, labelled as inference.
   - **Aging in progress**: in an in-progress status category longer than the threshold (use `listJiraIssueChangelogs` for a few to confirm).
   - **Orphans**: no parent epic where the project uses epics.
3. **Likely duplicates.** Group by near-identical summaries and shared components, then confirm candidates with a targeted search (`summary ~ "key terms"`). Report pairs or groups with similarity reasons and mark them "likely", never "duplicate", unless an existing link says so (`listJiraIssueLinkTypes`, issue links).
4. **Prioritize** the cleanup: what to fix first and why (for example high-priority items that are stale and unassigned).
5. **Output**: a summary table (finding, count, threshold, query), then detail tables with issue keys as links, then suggested actions per group written as proposals. Do not close, reassign, relabel or link anything.
6. If the user wants changes applied, prepare the exact list and follow `write-safety`.

## What to watch for

- State the scope and the date of the snapshot; backlogs change.
- If the result set was capped, say the counts are lower bounds.
- Do not name individuals as the cause of stale work; report items, not people.
