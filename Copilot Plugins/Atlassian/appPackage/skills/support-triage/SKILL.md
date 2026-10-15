---
name: support-triage
description: Triage a support problem or incident by finding similar and known Jira issues, reading their comments and history, and matching Confluence knowledge-base articles, then recommending the next step with sources. Use when asked to triage, diagnose, or find similar cases, known issues, workarounds or fixes for an error or incident.
---

# Support Triage

Read-only. Combine Jira history and the Confluence knowledge base into a sourced triage summary. Apply `atlassian-access`, `jql-cql-builder` and `evidence-and-uncertainty` first.

## Inputs to confirm

- The symptom or error text, the product component or area, the environment, the customer or site, the version or release, and when it started. If the error text or component is missing, ask for it before searching.
- Which Jira project(s) and Confluence space(s) to use; take defaults from the `jql-cql-builder` reference.

## How to do it

1. **Extract the signature**: error code or message key phrase, affected component, environment, version, trigger. Keep key terms short.
2. **Search Jira for related issues** with `searchJiraIssuesUsingJql`. Run three queries and include resolved issues: by error signature, by component plus symptom, and by version or release if known. Sort by recency.
3. **Read the best matches** (not all): `getJiraIssue`, then the discussion via `discover` and `executeRead` for `listJiraIssueComments`, and the history via `listJiraIssueChangelogs` for status changes, reopen and resolution. For links to related work use `listJiraIssueLinkTypes` and `listJiraIssueRemoteIssueLinks`.
4. **Search the knowledge base** with `searchConfluence` (CQL on the KB space: text and title terms for the error, component, and "workaround", "known issue", "resolution"). Open the strongest pages with `getConfluenceContent` and note the version and last-modified date.
5. **Other sources.** If tools for chat or document sources (for example Teams or SharePoint) are available, search them for the same terms and list those results separately, labelled by source. If they are not available, say those sources were not checked.
6. **Judge** each match: same error and component and recent is a likely duplicate or known issue; same component with a different error is related; an old resolved match may be a regression. Note who resolved it and how only if stated in the issue.
7. **Respond** in this shape:
   - **Summary** (two or three sentences) and confidence.
   - **Likely known issue or duplicate**: key, status, resolution, link, why it matches.
   - **Related issues**: short table.
   - **Knowledge-base articles**: title, page ID, version, date, the relevant step or workaround.
   - **Suggested next step**: the safest action (apply the documented workaround, request a missing detail, escalate to a named team only if one is recorded).
   - **Gaps**: what could not be found or accessed.
8. If the user wants a comment, link or new issue, prepare the text only and follow `write-safety`.

## What to watch for

- Do not present a workaround that is not in a cited source.
- Do not assign an owner or team that is not recorded.
- Mark old or draft KB content as possibly outdated.
- Do not include customer-identifying detail beyond what the user already shared.
