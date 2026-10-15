---
name: release-notes
description: Draft release notes from a Jira project version - resolved issues grouped by type with links, known issues and upgrade notes taken only from the issues and Confluence pages. Use when asked for release notes, a changelog, what shipped in a version or release, or what is still open for a release.
---

# Release Notes

Read-only. Apply `atlassian-access`, `jql-cql-builder` and `evidence-and-uncertainty` first.

## Inputs to confirm

- The Jira project and the version (release) name, or a date range if there is no version. If the user gives only a project, list versions with `getJiraProjectVersions` (via `discover` and `executeRead` if it is not a primary tool) and ask which one.
- The audience (customers, internal, support) and the format (Markdown in chat is the default).

## How to do it

1. **Resolve the version.** Read it with `getJiraProjectVersions` and note its name, status (released or not) and release date. Optionally read related work links with `getJiraProjectVersionRelatedWork`.
2. **Query the issues.** With `searchJiraIssuesUsingJql`, run bounded queries and show each one:
   - shipped: `project = "KEY" AND fixVersion = "name" AND statusCategory = Done ORDER BY issuetype, priority DESC`
   - not done: `project = "KEY" AND fixVersion = "name" AND statusCategory != Done`
   - Request key, summary, issuetype, status, resolution, priority, labels, components. Page until complete and report the count only from the total or the paged result.
3. **Group** shipped issues by type (features, improvements, bug fixes, other) or by component if that reads better. Use the issue summary as the base text and tighten it only when meaning is unchanged. If the project has a release-note field or label [[CONFIGURE: release note field or label]], prefer it.
4. **Known issues and upgrade notes.** Include only items the issues, linked pages or comments explicitly state. Search Confluence with `searchConfluence` for release or upgrade pages for the version and cite them. If nothing is stated, write "None recorded" rather than inventing notes.
5. **Flag gaps.** List issues with no resolution, vague summaries, missing components, or fixVersion set but status not done, so the user can fix them before publishing.
6. **Output** the notes with the version, date, a short summary line, grouped sections with issue keys as links, known issues, upgrade notes, and a Sources and queries appendix. Mark the draft "unreviewed".
7. To publish to Confluence, follow `write-safety` (draft page only).

## What to watch for

- Do not include internal-only issues or customer names unless the audience calls for it; ask if labels or security levels suggest an issue is internal.
- Do not describe behavior the issue does not state.
- Do not promise dates for unreleased versions beyond what the version record shows.
