---
name: meeting-actions-to-jira
description: Extract action items with owners from meeting notes (pasted or in Confluence) and, after the user previews and confirms, create Jira tasks with the right assignees. Use when asked to turn meeting notes or action items into Jira tasks, tickets or follow-ups.
---

# Meeting Actions to Jira

Reads freely; creating issues is a write and needs the Jira write permission to be granted. Apply `atlassian-access`, `jql-cql-builder`, `evidence-and-uncertainty` and `write-safety` first.

## How to do it

1. **Get the notes.** From a Confluence URL, call `getConfluenceContent` with the page ID, `content_format="markdown"` and `detail="full"`. Otherwise use the pasted text.
2. **Find action items with an owner.** Common shapes: `@Sarah to ...`, `Mike will ...`, `Action: Lisa - ...`, `TODO: ... (Sam)`, `- Name: ...`. For each record the owner name, the task in the original wording, due date if stated, and the meeting title and date. Merge obvious duplicates and flag items with no owner. Do not invent a due date or owner.
3. **Choose the project.** Ask which Jira project to use. If unsure, `discover` then run `listJiraProjects` with `inputs={"action": "create"}`. If the notes name a key, confirm it exists in that list.
4. **Resolve assignees** once per person with `lookupJiraAccountId` and `inputs={"query": "<name>"}`; try the full name, then the first name. If there is no match offer to create the task unassigned, skip it, or try another spelling. If several match, ask which person. Never guess an account ID.
5. **Issue type.** Run `listJiraProjectIssueTypesMetadata` with `projectIdOrKey` and use Task when it exists (Story or Bug only when the item clearly is one).
6. **Preview and confirm** (per `write-safety`): a table of summary, assignee, issue type, due date, and the description text, plus the run marker and the duplicate check result. Do not create anything until the user explicitly confirms; apply their edits and show the preview again.
7. **Create** with `createJiraIssue`, one at a time: `projectKey`, `issueType`, an action-verb summary, `assignee` set to the account ID, and a description with the task, meeting context, a link to the notes and the original quote. If creation fails on required fields, run `getJiraIssueTypeMetaWithFields`, ask for the values and retry that item only.
8. **Audit and verify.** Add the audit note, re-read each created issue, and summarize with keys, assignees and links, noting any items skipped or failed.

## What to watch for

- If no action items with owners are found, say so, list the patterns you looked for and offer to create tasks from items the user points to.
- Names that appear in different forms may be one person; confirm before looking up twice.
- If the write tool is unavailable or denied, give the user the task list as text to enter manually.
- Do not include sensitive discussion from the notes in issue descriptions beyond the action itself.
