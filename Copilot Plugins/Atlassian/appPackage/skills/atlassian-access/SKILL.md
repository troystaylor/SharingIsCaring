---
name: atlassian-access
description: Shared rules for every Jira, Confluence and Bitbucket call through the Atlassian connector - get the cloudId first, choose between direct tools and discover then execute, paginate, handle denied or missing access, avoid forbidden tools, and cite sources. Use whenever a task needs Atlassian data, before running any other Atlassian skill.
---

# Atlassian Access

Apply this skill before any Jira, Confluence or Bitbucket call. Other Atlassian skills assume it.

## Bootstrap (always first)

1. Call `getAccessibleAtlassianResources`. Every other call needs a `cloudId`, and this returns it with the site URL. If more than one site is returned, ask which one and reuse the answer for the rest of the task.
2. Call `atlassianUserInfo` when the task depends on "me" (my issues, my worklogs, who is asking).
3. Never invent a `cloudId`, project key, space key or account ID. Resolve them from tool output.

## Choosing tools

- Primary tools are visible in the tool list and called directly (for example `searchJiraIssuesUsingJql`, `getJiraIssue`, `searchConfluence`, `getConfluenceContent`).
- Everything else is reached in two steps: `discover` (describe the goal in plain language; it returns the exact operation `name` and `inputs`), then an execute tool with `name`, `cloudId` and a flat `inputs` object. Skip `discover` when you already know the operation name.
- Match the execute tier to the operation: `executeRead` for get, list, search, diff and export; `executeWrite` for create, edit, comment and transition. Never use `executeDestructive` in these skills.
- `cloudId` is a top-level argument beside `name` and `inputs`, never inside `inputs` (the exception is operations that take none, such as `getContentFormatGuide`).
- Use the exact parameter names from the live schema or the `discover` result. Unrecognized parameters are silently dropped, so a wrong name returns a normal-looking result that ignored your filter. If a result looks unfiltered, re-check the names.
- Keep tool calls minimal and targeted. Prefer JQL and CQL (see `jql-cql-builder`) over semantic `search`, which uses Rovo credits, as do the Teamwork Graph tools. If you must use them, say so in the Sources footer.

## Pagination and counts

- Request a bounded page (for example 25 to 100 results) and follow the next-page token or cursor the tool returns until you have what the task needs or hit a stated cap.
- Report counts only from a total the tool returns or from paging to the end. If neither is possible, say the count is a lower bound. Never estimate.

## When access is denied or empty

- A 401, 403, "not found" or empty result can mean the user lacks permission, the tool's permission group is disabled by an Atlassian admin, the OAuth scope was not granted, or an IP allowlist blocked the call. Say which it could be and that you cannot tell from here.
- Do not retry with guessed identifiers or broaden the query to work around a denial. Report the limit and, where useful, tell the user who can grant access (an Atlassian site or org admin).
- Bitbucket tools work only for Bitbucket Cloud workspaces linked to an Atlassian organization.

## Not allowed in these skills

- Any `delete_jira` or `manage_jira` tool (deleteJiraIssue, deleteJiraComment, deleteJiraIssueAttachment, createJiraProject, updateJiraProject, workflow and screen tools). They are disabled by default and must stay that way.
- Confluence permission and sharing tools: `addConfluenceContentPermissions`, `removeConfluenceContentPermissions`, `replaceConfluenceContentPermissions`, `setConfluenceContentRestrictionState`, `enableConfluencePublicLink`, `disableConfluencePublicLink`.
- Credit-heavy generators: `createConfluenceInfographicForPage`, `editConfluenceInfographicForPage`, `createConfluenceMauiApp`, `editConfluenceMauiApp`.
- Structural changes: creating or archiving spaces, and moving, copying or archiving content.
- Bitbucket merge, approve, branch, commit and pipeline-run tools.
If a user asks for one of these, decline and point them to Atlassian directly.

## Citation format

Cite every claim with the identifier a person can open:

- Jira: `KEY-123` with status and last-updated date, plus the issue URL. Comments: issue key and comment ID or author and date.
- Confluence: page title, page ID, version number, last-modified date, space key, and URL.
- Bitbucket: `workspace/repo`, branch or commit SHA, file path with line range, or pull request number.

End each answer with a Sources list in this format and, if any, a line `Rovo-credit calls: N`.

Also apply `evidence-and-uncertainty`. Any write goes through `write-safety`.
