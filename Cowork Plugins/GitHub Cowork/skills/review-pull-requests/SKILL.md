---
name: review-pull-requests
description: |
  Finds, reads, reviews, comments on, and merges GitHub pull requests.
  Use when the user asks "what PRs are waiting on me", "review this pull request",
  "what's in PR 412", "approve that PR", "request changes", "leave a comment on
  the PR", "is this ready to merge", "merge it", "what's blocking this PR",
  "are the checks passing", or names a pull request and asks about its status,
  diff, reviewers, or CI results.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
---

# Review GitHub Pull Requests

## What This Skill Does

Surfaces the pull requests that need a decision, gathers everything needed to
make that decision (diff, checks, existing reviews, conversation), and then
records the decision — approve, request changes, comment, or merge.

## When to Activate

- User asks what pull requests are waiting on them or their team
- User wants a summary of what a pull request changes
- User asks whether CI is passing or what is blocking a merge
- User wants to approve, request changes, comment on, or merge a PR
- User wants to open a pull request from an existing branch

## Workflow

### Finding

1. **Scope the search.** `list_pull_requests` for one repo, `search_pull_requests`
   for anything cross-repo or qualifier-based.

   > `search_pull_requests(query: "org:contoso is:pr is:open draft:false review-requested:@me")`

2. **Prioritize.** Lead with PRs where the user is a requested reviewer, then
   ones they authored that are blocked, then everything else. Sort stale work
   to the top when the user is doing a sweep.

### Understanding

3. **Gather context with `pull_request_read`.** It has one required shape:
   `method`, `owner`, `repo`, `pullNumber`. Note the camelCase `pullNumber` —
   PR tools use it, issue tools use `issue_number`.

   | Question | `method` |
   |---|---|
   | What is this PR? | `get` |
   | What changed? | `get_diff` (full patch) or `get_files` (file list, paginated) |
   | Is CI green? | `get_check_runs`, or `get_status` for the combined commit status |
   | Who reviewed and what did they say? | `get_reviews` |
   | What are the unresolved code comments? | `get_review_comments` |
   | What is the general discussion? | `get_comments` |
   | What commits are included? | `get_commits` |

4. **Prefer `get_files` over `get_diff` for large PRs.** A full diff on a big
   change floods the context. Get the file list first, then pull the diff only
   if the user wants a real code review.

5. **Summarize the change, not the diff.** Say what it does, what could break,
   and what needs a human eye. Do not paste the patch back.

### Deciding

6. **Never approve or merge without explicit user instruction.** Summarizing a
   PR is safe; approving one is the user putting their name on it. Ask.

7. **Record the review with `pull_request_review_write`.**

   | Intent | Call |
   |---|---|
   | Approve | `method: "create"`, `event: "APPROVE"`, `body` |
   | Request changes | `method: "create"`, `event: "REQUEST_CHANGES"`, `body` |
   | Comment without a verdict | `method: "create"`, `event: "COMMENT"`, `body` |
   | Draft a multi-comment review | `method: "create"` with no `event`, then `add_comment_to_pending_review` per line, then `method: "submit_pending"` |
   | Discard a draft review | `method: "delete_pending"` |
   | Resolve a thread | `method: "resolve_thread"`, `threadId` from `get_review_comments` |

8. **Reply in the right place.** `add_reply_to_pull_request_comment` replies to
   a specific review comment. `add_issue_comment` (with the PR number) posts to
   the main conversation. Use the former for code discussion.

9. **Merge only after checks and approvals are confirmed.** Re-read
   `get_check_runs` and `get_reviews` immediately before merging, then call
   `merge_pull_request` with an explicit `merge_method` (`merge`, `squash`, or
   `rebase`) and a `commit_title` that follows the repo's convention.

10. **Handle a stale branch.** If the PR is behind its base, use
    `update_pull_request_branch` before merging.

### Opening

11. **`create_pull_request`** needs `owner`, `repo`, `title`, `head`, `base`.
    Confirm the branch exists with `list_branches` first. Use
    `update_pull_request` to change the title, body, base, or draft state later.

## Output Format

### Review queue

**5 pull requests waiting on you**

| PR | Repo | Author | Age | Checks | Size |
|---|---|---|---|---|---|
| [#412](https://github.com/contoso/billing-api/pull/412) Fix partial refund rounding | billing-api | jchen | 2d | Passing | +42 −11 |
| [#88](https://github.com/contoso/web/pull/88) Migrate to React 19 | web | mpatel | 9d | 1 failing | +2,104 −1,876 |

**#88 has been open 9 days with a failing check** — probably the one to look at
first. Want me to dig into it?

### PR summary

**contoso/billing-api#412 — Fix partial refund rounding**
by jchen · opened 2 days ago · `bugfix/refund-rounding` → `main`

**What it does:** Replaces floating-point arithmetic in the partial refund
path with integer cents, and adds a regression test for the 0.005 case
reported in #398.

**Files changed (3)**
- `src/billing/refund.ts` (+31 −9) — the fix
- `test/refund.test.ts` (+11 −0) — regression test
- `CHANGELOG.md` (+2 −0)

**Checks:** 4 of 4 passing
**Reviews:** 1 approval (mpatel) · no changes requested
**Blocking:** nothing

**Worth a look:** the rounding mode changes from half-up to banker's rounding
for amounts ending in exactly half a cent. That is a behavior change, not just
a bug fix — confirm it is intended before approving.

### After a write

Approved [contoso/billing-api#412](https://github.com/contoso/billing-api/pull/412)
with the note about banker's rounding. It is now mergeable — say the word and
I'll squash-merge it.

## Handling Edge Cases

- **Merging is irreversible in practice.** Always confirm the PR number, the
  merge method, and the target branch back to the user before calling
  `merge_pull_request`.
- **Merge conflicts:** `merge_pull_request` fails when the PR is not mergeable.
  Report the reason from the error rather than retrying.
- **Failing checks:** Do not merge over a red build without the user explicitly
  acknowledging which check is failing.
- **Required reviews:** A repo may have branch protection requiring N approvals
  or a code-owner review. A 405 or 422 on merge usually means protection rules,
  not a bug — explain that.
- **Self-approval:** GitHub rejects approving your own PR. If the user authored
  the PR, say so instead of attempting the call.
- **Draft PRs:** Cannot be merged. Offer `update_pull_request(draft: false)`
  first.
- **Huge diffs:** Above a few hundred lines, review by file rather than pulling
  the whole patch, and tell the user you are sampling rather than reading
  everything.
- **PR vs. issue numbering:** They share a number space per repo. `#412` may be
  an issue. If `pull_request_read` 404s, try `issue_read`.

## Handling Authentication

If a tool call fails because the user has not connected to GitHub yet:

1. Tell the user: "I need to connect to GitHub to see that pull request. You
   should see a sign-in prompt — please complete it and I'll continue."
2. Do NOT retry the tool call immediately. Wait for confirmation.
3. If reads work but `pull_request_review_write` or `merge_pull_request`
   returns 403, the user lacks write access on that repository. Say that
   directly and offer to draft the review text for them to post themselves.
4. For organization repositories that return 404 despite the user having
   access, the GitHub OAuth app may need org admin approval. Suggest they
   check with their GitHub org admin.
