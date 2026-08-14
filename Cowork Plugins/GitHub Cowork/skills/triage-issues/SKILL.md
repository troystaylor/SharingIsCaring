---
name: triage-issues
description: |
  Finds, reads, creates, labels, assigns, and comments on GitHub issues.
  Use when the user asks to "triage the backlog", "what issues are open",
  "file an issue", "open a bug for", "assign this to", "label these",
  "close that issue", "add a comment to issue", "what's untriaged",
  "who's working on", "link this as a sub-issue", or asks about issue
  status, priority, or ownership in a GitHub repository.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
---

# Triage GitHub Issues

## What This Skill Does

Turns an unstructured backlog into a decided one. Finds issues that need
attention, reads the full context, and applies the outcome — labels,
assignees, comments, milestones, sub-issue links, or closure.

## When to Activate

- User wants to run through untriaged, stale, or unassigned issues
- User wants to file a new issue from a conversation, bug report, or document
- User wants to label, assign, prioritize, or close existing issues
- User asks who owns an issue or what its current status is
- User wants to break a large issue into sub-issues

## Workflow

### Reading and finding

1. **Resolve the repository or org scope.** Ask if it is not clear. "Which
   repo?" is a better question than triaging the wrong backlog.

2. **Find the issues.** Use `list_issues` for a single repo with simple
   filters, or `search_issues` when the query spans repos or needs qualifiers.

   > `search_issues(query: "org:contoso is:issue is:open no:label sort:created-asc")`
   > `list_issues(owner: "contoso", repo: "billing-api", state: "open", labels: ["bug"])`

3. **Read before deciding.** Use `issue_read` to get the body, comments,
   labels, assignees, and linked PRs before recommending an action. Never
   label or close an issue based on the title alone.

   > `issue_read(owner: "contoso", repo: "billing-api", issue_number: 412)`

### Acting

4. **Confirm before every write.** Show the user exactly what you are about to
   change and get an explicit yes. Batch the confirmation — present the whole
   triage plan as a table, then apply it after approval, rather than asking
   once per issue.

5. **Apply the change with `issue_write`.** The same tool creates and updates.

   | Intent | Call |
   |---|---|
   | File a new issue | `issue_write(method: "create", owner, repo, title, body, labels, assignees)` |
   | Label / assign / retitle | `issue_write(method: "update", owner, repo, issue_number, labels, assignees)` |
   | Close | `issue_write(method: "update", owner, repo, issue_number, state: "closed", state_reason: "completed")` |

6. **Comment when a decision needs a rationale.** Use `add_issue_comment` so
   the reasoning lives on the issue, not just in the Cowork conversation.

   > `add_issue_comment(owner: "contoso", repo: "billing-api", issue_number: 412, body: "Closing as duplicate of #398.")`

7. **Link related work.** Use `sub_issue_write` to attach or detach sub-issues
   when breaking down an epic.

8. **Report back.** Summarize what changed with links, and flag anything you
   deliberately did not touch.

### Validating labels and fields before writing

Do not invent label names or issue types — a write with an unknown label fails.

- `get_label` confirms a specific label exists in the repo
- `list_issue_types` returns the org's issue types (Bug, Feature, Task) if enabled
- `list_issue_fields` returns custom fields available on issues

If a label the user asked for does not exist, say so and offer the closest
existing one rather than creating a new taxonomy on the fly.

## Output Format

### Triage plan (before applying)

Found **8 untriaged issues** in `contoso/billing-api`. Proposed triage:

| # | Title | Age | Proposed label | Proposed assignee |
|---|---|---|---|---|
| [#412](https://github.com/contoso/billing-api/issues/412) | Rounding error on partial refunds | 3d | `bug`, `p1` | jchen |
| [#409](https://github.com/contoso/billing-api/issues/409) | Add CSV export to invoices | 11d | `enhancement` | — |
| [#398](https://github.com/contoso/billing-api/issues/398) | Docs: retry behavior unclear | 26d | `documentation` | — |

Two more (#377, #351) look like duplicates of #412 — I'd close them with a
pointer. Want me to apply all of this?

### After applying

Applied to `contoso/billing-api`:

- **#412** → labeled `bug`, `p1`; assigned to jchen
- **#409** → labeled `enhancement`
- **#398** → labeled `documentation`
- **#377, #351** → closed as duplicates of #412, with a comment on each

Not changed: **#404** — it needs a product decision before it can be labeled.

### New issue

Opened [contoso/billing-api#417](https://github.com/contoso/billing-api/issues/417)
— "Partial refunds round to the wrong cent" · `bug`, `p1` · assigned to jchen

## Handling Edge Cases

- **Destructive actions:** Closing issues, reassigning away from a current
  assignee, and removing labels are all destructive. Always confirm first,
  and never close more than one issue without listing each one.
- **Ambiguous target:** If the user says "close that one" and several issues
  are in play, restate which issue you mean and wait.
- **Issue vs. PR:** GitHub treats pull requests as issues in some APIs. If a
  result is a PR, hand it to the pull request skill rather than triaging it
  as an issue.
- **Write permission denied:** The user may have read-only access to the repo.
  Say so plainly and offer to draft the issue text for them to file manually.
- **Bulk operations:** For more than ~10 writes, apply in batches and report
  progress so a failure part-way through is visible.
- **Duplicate detection:** Before filing a new issue, run `search_issues` on
  the key terms. If a likely duplicate exists, show it and ask before creating.
- **Labels not in the default toolset:** Creating new labels requires the
  `labels` toolset. With the default connector you can apply existing labels
  via `issue_write` but not create new ones.

## Handling Authentication

If a tool call fails because the user has not connected to GitHub yet:

1. Tell the user: "I need to connect to GitHub to work with those issues.
   You should see a sign-in prompt — please complete it and I'll continue."
2. Do NOT retry the tool call immediately. Wait for the user to confirm.
3. If a read succeeds but a write returns 403, the connection is working but
   lacks write permission on that repository. Say that specifically — it is a
   permissions problem, not a sign-in problem.
4. If an organization's repositories are inaccessible, the GitHub org may
   require admin approval for the OAuth app. Suggest the user contact their
   GitHub org admin.

## Additional Resources

- **`references/issue-tool-reference.md`** — `issue_write` methods, state
  reasons, and field-by-field parameter notes
