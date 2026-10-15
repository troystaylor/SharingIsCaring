---
name: my-atlassian-queue
description: Show what needs the current user's attention in Jira and Confluence - assigned work, items due or overdue, issues they reported that moved, watched items and pages that mention them. Use when asked for my Jira tasks, my day, what is on my plate, or what changed on my issues.
---

# My Atlassian Queue

Read-only personal work queue for the signed-in user. Apply `atlassian-access`, `jql-cql-builder` and `evidence-and-uncertainty` first.

## How to do it

1. Call `getAccessibleAtlassianResources` (ask which site if several) and `atlassianUserInfo` for the user's name and account ID.
2. Gather in parallel with `searchJiraIssuesUsingJql` (request key, summary, status, priority, duedate, updated, project; `maxResults` 25 each):
   - assigned and open: `assignee = currentUser() AND statusCategory != Done ORDER BY priority DESC, duedate ASC, updated DESC`
   - due or overdue: `assignee = currentUser() AND statusCategory != Done AND duedate <= 7d ORDER BY duedate ASC`
   - reported by me and recently updated: `reporter = currentUser() AND updated >= -3d ORDER BY updated DESC`
   - watching and active: `watcher = currentUser() AND statusCategory != Done AND updated >= -3d ORDER BY updated DESC`
3. Add Confluence with `searchConfluence`, for example pages that mention the user: `mention = currentUser() AND lastmodified >= now("-14d") ORDER BY lastmodified DESC`. If that returns nothing useful, say so rather than guessing a different query.
4. Present, most urgent first: overdue and due this week; high-priority assigned work; items waiting on others or recently updated; pages that need a reply. For each give the key or title, status, priority, due date and link. Keep to the top items and give counts for the rest, with the query behind each count.
5. Offer next steps such as opening an issue in detail (see `issue-brief`) or drafting a comment as text, without making changes.

## What to watch for

- Read-only; any change follows `write-safety`.
- Show dates in the user's time zone and state the windows used.
- If a query returns nothing, say so rather than padding the list.
- Not for project-wide reports; use `status-reporting` or `backlog-hygiene`.
