---
name: release-notes
description: |
  Drafts release notes, changelogs, and "what shipped" summaries from merged
  pull requests, commits, and closed issues. Use when the user asks to
  "write release notes", "draft a changelog", "what shipped in v2.1",
  "summarize what changed since the last release", "what's new in this release",
  "prepare the release announcement", or "what went out this week".
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: aggregation
---

# Draft GitHub Release Notes

## What This Skill Does

Turns merged work into something a human wants to read. Collects the pull
requests, commits, and issues in a release window and writes notes at the right
altitude for the audience — engineers, customers, or an internal announcement.

## When to Activate

- User is preparing a release and needs notes or a changelog entry
- User asks what shipped in a version, a date range, or since the last release
- User wants a weekly or sprint "what went out" summary
- User needs a customer-facing announcement drawn from engineering work

## Workflow

1. **Establish the window.** Every release note needs a start and an end.

   | User said | How to resolve |
   |---|---|
   | "since the last release" | `get_latest_release` → use its `published_at` as the start |
   | "in v2.1" | `get_release_by_tag(tag: "v2.1")` and the release before it from `list_releases` |
   | "this week" / "last sprint" | Compute the absolute ISO dates yourself — GitHub search does not understand relative dates |
   | nothing | Ask. Do not guess the window. |

2. **Confirm the audience before writing.** Engineering changelog, customer
   release note, and exec summary are three different documents. Ask which one
   if it is not obvious from context.

3. **Collect merged pull requests — the primary source.** PR titles are written
   for humans; raw commits usually are not.

   > `search_pull_requests(query: "repo:contoso/billing-api is:pr is:merged base:main merged:2026-07-15..2026-08-14")`

4. **Fill gaps with commits and issues.** Use `list_commits` for direct pushes
   that never went through a PR, and `search_issues` with
   `is:issue is:closed closed:START..END` for fixes tracked as issues.

5. **Read the ones that matter.** For anything that looks like a breaking
   change, migration, or headline feature, call `pull_request_read`
   (`method: "get"`) to get the body. PR descriptions usually contain the
   migration notes and the reasoning.

6. **Classify each change.** Use labels and title prefixes as signals, but
   apply judgment — a `fix:` that changes an API contract is a breaking change
   regardless of its prefix.

   | Category | Signals |
   |---|---|
   | Breaking changes | `breaking`, `!` in a conventional commit, major version bump, API/schema changes |
   | New features | `feat:`, `enhancement`, `feature` |
   | Fixes | `fix:`, `bug`, links to a closed bug issue |
   | Performance | `perf:`, `performance` |
   | Security | `security`, Dependabot merges, CVE references |
   | Internal | `chore:`, `refactor:`, `ci:`, `test:`, docs-only |

7. **Write for the audience.** Rewrite each PR title into a sentence that says
   what changed for the reader. "Fix rounding on partial refunds (#412)" becomes
   "Partial refunds no longer lose a cent on amounts ending in half a cent."

8. **Lead with breaking changes.** Anything requiring action from the reader
   goes first, with the migration step spelled out.

9. **Attribute contributors.** List distinct PR authors, and call out
   first-time contributors when you can tell.

10. **Hand back a draft, not a published release.** Produce the markdown for
    the user to review. Do not attempt to publish — creating GitHub releases is
    outside this connector's toolset.

## Output Format

### Customer-facing release note

## v2.1.0 — August 14, 2026

**Breaking changes**

- **Partial refunds now use banker's rounding.** Amounts ending in exactly half
  a cent round to even instead of up. If you reconcile against our totals,
  expect sub-cent differences on affected transactions. ([#412](https://github.com/contoso/billing-api/pull/412))

**New**

- Export invoices to CSV from the billing dashboard ([#409](https://github.com/contoso/billing-api/pull/409))
- Webhook retries are now configurable per endpoint ([#401](https://github.com/contoso/billing-api/pull/401))

**Fixed**

- Timezone handling on invoice due dates for non-UTC accounts ([#405](https://github.com/contoso/billing-api/pull/405))
- Duplicate webhook deliveries under high load ([#398](https://github.com/contoso/billing-api/pull/398))

**Security**

- Updated `stripe` to 14.2.0, resolving CVE-2026-1234 ([#409](https://github.com/contoso/billing-api/pull/409))

Thanks to @jchen, @mpatel, and @rsantos.

### Engineering changelog

| Category | PR | Title | Author |
|---|---|---|---|
| Breaking | [#412](https://github.com/contoso/billing-api/pull/412) | Use integer cents in refund path | jchen |
| Feature | [#409](https://github.com/contoso/billing-api/pull/409) | Add CSV invoice export | mpatel |
| Fix | [#405](https://github.com/contoso/billing-api/pull/405) | Correct due-date timezone offset | rsantos |
| Internal | [#403](https://github.com/contoso/billing-api/pull/403) | Bump CI to Node 22 | jchen |

**23 PRs merged** · 3 contributors · 4,102 additions, 1,988 deletions

### Weekly "what shipped"

**Week of Aug 8–14 · contoso org**

- **billing-api** — 8 PRs. Refund rounding fix shipped; CSV export behind a flag.
- **web** — 3 PRs. React 19 migration still open (#88, 9 days).
- **infra** — 2 PRs. Both dependency bumps.

Nothing shipped in **contoso/mobile** this week.

## Handling Edge Cases

- **No releases in the repo:** Fall back to a date range or a commit range and
  say that is what you did.
- **No merged PRs in the window:** Say so plainly. Check `list_commits` in case
  the team pushes directly to the default branch.
- **Squash-merge repos:** Commits on `main` map 1:1 to PRs. Prefer the PR view.
- **Monorepos:** Group by the top-level path from `pull_request_read`
  (`method: "get_files"`) so readers can find their component.
- **Noise:** Dependabot and automated PRs can dominate the count. Group them
  into a single "Dependency updates (14 PRs)" line unless one carries a
  security fix worth naming.
- **Multiple repos:** Ask whether the user wants one combined note or one per
  repo before generating.
- **Large windows:** Over ~100 PRs, summarize by category and theme rather than
  listing every PR, and say you have done so.

## Handling Authentication

If a tool call fails because the user has not connected to GitHub yet:

1. Tell the user: "I need to connect to GitHub to pull the merged work for
   that release. You should see a sign-in prompt — please complete it and
   I'll continue."
2. Do NOT retry the tool call immediately. Wait for confirmation.
3. If some repositories return results and others 404, the connection is
   working but does not cover every repo. Name the repos you could not read so
   the notes are not silently incomplete.
