---
name: delegate-to-copilot
description: |
  Hands implementation work to the GitHub Copilot coding agent and tracks it.
  Use when the user says "have Copilot fix this", "assign it to Copilot",
  "let Copilot take that issue", "get Copilot to open a PR for", "delegate this
  to the coding agent", "ask Copilot to review this PR", or "what's the status
  of the Copilot job".
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
---

# Delegate Work to GitHub Copilot

## What This Skill Does

Routes well-scoped implementation work to the GitHub Copilot coding agent,
which works asynchronously in GitHub and opens a pull request for a human to
review. Also requests automated Copilot reviews on existing pull requests.

The agent is not a chat reply. It runs in the background, sometimes for many
minutes, and its output is a PR that still needs review.

## When to Activate

- User wants an existing issue implemented by Copilot rather than a person
- User describes a change and wants a PR opened for it without doing the work
- User wants a first-pass automated review on a pull request
- User asks about the progress of work already handed to Copilot

## Workflow

### Deciding whether to delegate

1. **Check that the task is a good fit.** The coding agent does well on
   well-scoped, low-ambiguity changes in a codebase with tests. It does badly
   on vague requests, cross-cutting architecture, and anything where the
   requirements live in someone's head.

   | Good fit | Poor fit |
   |---|---|
   | "Add pagination to the invoices endpoint" | "Improve performance" |
   | "Fix the timezone bug in #405" | "Redesign the billing module" |
   | "Add tests for the refund path" | "Decide how we should handle refunds" |
   | Bounded, testable, one component | Requires product decisions or spans many services |

   If the task is a poor fit, say so and offer to sharpen it first — usually by
   writing a proper issue with acceptance criteria.

2. **Make sure the task is written down properly.** The agent works from the
   issue body or problem statement. Vague input produces a vague PR. Read the
   issue first with `issue_read` and, if it is thin, offer to improve the body
   before assigning.

### Delegating

3. **Prefer assigning an existing issue.** It keeps the work traceable.

   > `assign_copilot_to_issue(owner: "contoso", repo: "billing-api", issue_number: 405)`

   Optional parameters worth using:
   - `base_ref` — start from a branch other than the repo default
   - `custom_instructions` — constraints not captured in the issue body
     ("keep the public API unchanged", "add tests in `test/refund.test.ts`")

4. **Use `create_pull_request_with_copilot` when there is no issue** and the
   user wants to go straight from a described task to a PR. Give it the
   repository and a problem statement that reads like a good issue: what is
   wrong, what "done" looks like, and any constraints.

5. **Always confirm before dispatching.** This consumes a Copilot premium
   request and creates real activity in the repository. Show the user the
   repository, the issue or problem statement, and any custom instructions,
   and get an explicit yes.

6. **Set expectations after dispatch.** Tell the user it runs asynchronously,
   the result is a draft PR that needs review, and that you can check on it.

7. **Track it.** Use `get_copilot_job_status` to check progress, and look for the
   draft pull request the agent opens. Do not poll in a loop — check once,
   report, and offer to check again later. When the PR exists, hand off to the
   pull request review skill.

### Automated review

8. **`request_copilot_review`** adds Copilot as a reviewer on an existing PR.
   Note the camelCase `pullNumber`.

   > `request_copilot_review(owner: "contoso", repo: "billing-api", pullNumber: 412)`

   Frame it as a first pass, not a substitute for human review. Suggest it
   before requesting a human reviewer, not instead of one.

## Output Format

### Before dispatching

Ready to hand this to the Copilot coding agent:

**Repository:** contoso/billing-api
**Issue:** [#405](https://github.com/contoso/billing-api/issues/405) — Invoice due dates use the wrong timezone offset
**Starting branch:** `main`
**Extra instructions:** Keep the public API response shape unchanged; add a
regression test for the `America/Chicago` case.

This uses a Copilot premium request and will open a draft PR in the repo.
Shall I go ahead?

### After dispatching

Assigned [contoso/billing-api#405](https://github.com/contoso/billing-api/issues/405)
to the Copilot coding agent.

It works in the background — typically a few minutes to half an hour — and will
open a draft pull request when it is done. Ask me to check the status any time,
and I'll review the PR with you when it lands.

### Status check

**contoso/billing-api#405** — Copilot coding agent: in progress (started 8 minutes ago)

No pull request yet. Want me to check again in a bit?

### Not a good fit

"Make the billing module faster" is too open-ended for the coding agent — it
would have to guess what "faster" means and where the cost is, and you would
get a PR you have to reverse-engineer.

Two options:
1. I profile it first via the repo's existing benchmarks and we scope a
   specific change, then delegate that.
2. I draft an issue with concrete acceptance criteria (for example, "invoice
   list endpoint p95 under 200ms with 10k invoices") and we hand that over.

Which would you prefer?

## Handling Edge Cases

- **Copilot coding agent not enabled:** The agent requires a Copilot plan that
  includes it and repository-level enablement. If the call fails, say that it
  looks like the coding agent is not available for that repository and suggest
  the user check with their GitHub admin — do not retry.
- **Premium request cost:** Each dispatch consumes quota. Never fire one
  speculatively or as part of a batch without per-item confirmation.
- **Already assigned:** If the issue already has Copilot assigned, say so and
  check status instead of dispatching again.
- **Long-running jobs:** Do not block the conversation waiting. Report the
  current status and move on.
- **The PR still needs review:** Never merge a Copilot-authored PR without a
  human reviewing it. Treat it exactly like any other PR from a contributor.
- **Public repositories:** Anything in the issue body or custom instructions
  becomes public. Warn before including internal details on a public repo.
- **Bulk delegation:** If the user asks to assign many issues at once, confirm
  each one — the quota and the review burden are both per-issue.

## Handling Authentication

If a tool call fails because the user has not connected to GitHub yet:

1. Tell the user: "I need to connect to GitHub to hand this to the Copilot
   coding agent. You should see a sign-in prompt — please complete it and I'll
   continue."
2. Do NOT retry the tool call immediately. Wait for confirmation.
3. A 403 after a successful sign-in usually means write access is missing on
   that repository, or the Copilot coding agent is not enabled for it. Say
   which is more likely rather than reporting a generic failure.
