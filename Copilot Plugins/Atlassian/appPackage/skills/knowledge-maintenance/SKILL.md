---
name: knowledge-maintenance
description: Keep the Confluence knowledge base current from Jira work - take a resolved or changed Jira issue, find related pages, compare page versions, and prepare a draft update or comment for approval. Use when asked to update, correct or add knowledge-base content based on a Jira issue, to find stale articles, or to prepare a KB change request.
---

# Knowledge Maintenance

Reads freely and prepares changes. It does not write unless the user approves and `write-safety` is followed. Apply `atlassian-access`, `jql-cql-builder` and `evidence-and-uncertainty` first.

## Inputs to confirm

- The Jira issue key that triggered the update, and the KB space [[CONFIGURE: KB space key]].
- Whether the user wants a change package for approval (default), a Confluence comment, or an update draft.

## How to do it

1. **Read the issue.** `getJiraIssue`, then comments (`listJiraIssueComments`) and history (`listJiraIssueChangelogs`) through `discover` and `executeRead`. Extract the confirmed cause, resolution, workaround, affected versions and date. Use only what the issue states.
2. **Find related pages.** `searchConfluence` in the KB space using the error text, component and resolution terms. Open the candidates (`getConfluenceContent`, `detail` full, with metadata) and note title, ID, version and date.
3. **Check history.** For each page you may change, list versions with `listConfluenceContentVersions`, read the latest one or two with `getConfluenceContentVersion`, and compare with `diffConfluenceContentVersions` to see what changed recently and avoid reverting someone's edit.
4. **Decide per page**: still accurate, outdated, missing the new resolution, or no page exists. Cite the evidence (issue key, comment, version).
5. **Prepare the change package** for each affected page:
   - Page title, ID, current version and snapshot reference.
   - The exact text to add or replace, shown as a before and after.
   - Source: issue key, comment ID or date.
   - Risk note (for example, affects other versions or customers).
   - A new-page outline if none exists.
6. **Approval.** Approval is handled outside this skill (for example by a workflow your organization runs). Present the package in a form an approval flow can take: one row per change with page ID, version, proposed text, source and requester. Do not treat the user's chat reply as the organization's approval unless they say the approval is already granted.
7. **If approved and write scopes exist**, follow `write-safety`: re-read the page for a fresh `snapshotToken`, load space instructions, create the update as a draft or add a comment (`createConfluenceComment`) with the audit note, then verify by re-reading. Do not apply labels, status changes or edits not in the approved package.

## What to watch for

- Do not state a root cause the issue does not confirm.
- Do not remove content; mark it as proposed for removal in the package.
- If two pages cover the same topic, flag the duplicate instead of editing both.
