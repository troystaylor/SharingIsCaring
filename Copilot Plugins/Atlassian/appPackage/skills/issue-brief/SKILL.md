---
name: issue-brief
description: Summarize a single Jira issue - what it is, current state, a dated timeline from comments and status changes, linked work and open questions - with citations. Use when asked to summarize, explain, catch up on or brief someone on a specific Jira issue or ticket, or to get the history of an issue.
---

# Issue Brief

Read-only. Apply `atlassian-access` and `evidence-and-uncertainty` first.

## Inputs to confirm

- The issue key or URL. If only a description is given, find candidates with `searchJiraIssuesUsingJql` and ask which one; do not pick for the user.
- The audience and depth (one-paragraph catch-up, standard brief, or detailed history). Default: standard.

## How to do it

1. **Read the issue** with `getJiraIssue`. It reports the comment count but not comment bodies.
2. **Read the discussion and history** through `discover` and `executeRead`: `listJiraIssueComments` (page through if needed), `listJiraIssueChangelogs` (field changes, oldest first), and when relevant `listJiraIssueLinkTypes`, `listJiraIssueRemoteIssueLinks` and `listJiraIssueWorklogs`. Fetch only what the depth requires.
3. **Linked context.** For parents, epics and linked issues, read the key fields of the few that matter. If the issue mentions Confluence pages, read them with `getConfluenceContent` (`detail` full) and cite them.
4. **Compose** the brief:
   - **What it is**: summary and type in plain language.
   - **Current state**: status, assignee as recorded, priority, fix version, last update.
   - **Timeline**: dated bullets of key events (created, assignment changes, status transitions, decisions, workarounds, blockers), each cited by comment ID or changelog entry.
   - **Linked work**: parent, children, blocks and is-blocked-by, related pages.
   - **Open questions and next steps**: only those stated in the thread, plus clearly labelled inferences.
   - **Not seen**: attachments, restricted comments or links you could not read.
5. Offer a follow-up (draft a comment, a status update, or a handoff note) as text only. Writes follow `write-safety`.

## What to watch for

- Quote sparingly and redact secrets, tokens and personal data.
- Do not infer motives or blame; report what was recorded.
- If comments are paginated and you did not read all pages, say so.
