---
name: engineering-report
description: |
  Produces status reports, metrics, and rollups across GitHub repositories and
  teams. Use when the user asks for a "status update on the repo", "engineering
  report", "sprint summary", "how's the team doing", "what's the backlog look
  like", "PR throughput", "who's blocked", "review load", "stale work",
  "open bug count", or wants a written update for a standup, exec, or
  stakeholder audience.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: aggregation
---

# GitHub Engineering Report

## What This Skill Does

Aggregates issues, pull requests, and commit activity across one or many
repositories into a written status update — the kind a person has to assemble
by hand before a standup, a sprint review, or a stakeholder email.

## When to Activate

- User needs a status update, sprint summary, or standup note
- User asks how a repository, team, or project is tracking
- User wants backlog health, review load, throughput, or cycle-time signals
- User wants to know what is stale, blocked, or unowned
- User is writing an update for someone who does not read GitHub

## Workflow

1. **Pin down four things before querying.** Ask about anything missing rather
   than guessing — a report built on the wrong scope is worse than no report.

   | | Examples |
   |---|---|
   | Scope | one repo, an org, a list of repos, a team |
   | Window | last week, the sprint, since a date |
   | Audience | the team, a manager, an exec, a customer |
   | Question | "are we on track?", "what's stuck?", "how much shipped?" |

2. **Resolve the team if the report is people-scoped.** `get_teams` and
   `get_team_members` turn "the platform team" into usernames. `get_me`
   resolves "my" and "me". Compute absolute ISO dates from relative phrases
   before building any query.

3. **Run the queries that answer the question.** Prefer a few targeted searches
   over one broad one.

   | Signal | Query |
   |---|---|
   | Shipped | `search_pull_requests(query: "org:contoso is:pr is:merged merged:START..END")` |
   | In flight | `search_pull_requests(query: "org:contoso is:pr is:open draft:false")` |
   | Stuck in review | `search_pull_requests(query: "org:contoso is:pr is:open updated:<STALE_DATE")` |
   | Incoming work | `search_issues(query: "org:contoso is:issue created:START..END")` |
   | Backlog health | `search_issues(query: "org:contoso is:issue is:open no:assignee sort:created-asc")` |
   | Bug load | `search_issues(query: "org:contoso is:issue is:open label:bug")` |
   | Individual output | `search_pull_requests(query: "org:contoso is:pr is:merged author:USER merged:START..END")` |
   | Direct pushes | `list_commits(owner, repo, since, until)` |

4. **Compute the deltas, not just the totals.** "23 open bugs" says nothing.
   "23 open bugs, up from 18 two weeks ago" says something. Run the same query
   for the prior window whenever a trend claim is being made.

5. **Find the story.** A report that lists numbers is a dashboard. Identify the
   two or three things the reader should actually act on: the PR that has been
   open 9 days, the bug backlog growing faster than it is being burned down,
   the one person carrying all the review load.

6. **Separate fact from inference.** Report counts as counts. When you draw a
   conclusion ("review capacity looks like the bottleneck"), say that it is an
   inference from the data.

7. **Never present activity as performance.** PR and commit counts measure
   volume, not value. If the user asks for a per-person ranking, provide the
   data but note explicitly that these metrics do not measure individual
   productivity and are easily distorted by work type.

8. **Match the format to the audience.** Engineers get tables and PR numbers.
   Execs get three bullets and a risk. Ask if unsure.

## Output Format

### Sprint / weekly status

**contoso/billing-api — Aug 1–14, 2026**

**Shipped:** 23 PRs merged (18 the prior two weeks)
**In flight:** 5 open PRs, 2 waiting on review more than 3 days
**Backlog:** 47 open issues, 23 bugs (up from 18)

**Highlights**
- Partial refund rounding bug fixed and released in v2.1.0 (#412)
- CSV invoice export merged behind a feature flag (#409)

**Needs attention**
- [#88](https://github.com/contoso/web/pull/88) — React 19 migration, open 9
  days, 1 failing check, no reviewer assigned
- Bug backlog grew by 5 this sprint; intake is outpacing closure
- 12 open issues have no assignee, the oldest from March

### Cross-repo rollup

| Repo | Merged | Open PRs | Open issues | Stale (>7d) |
|---|---|---|---|---|
| billing-api | 23 | 5 | 47 | 2 |
| web | 11 | 8 | 62 | 4 |
| infra | 6 | 1 | 9 | 0 |
| **Total** | **40** | **14** | **118** | **6** |

**Where the risk is:** `web` has 8 open PRs against 11 merged — review is
backing up faster there than anywhere else.

### Exec summary

Engineering shipped 40 changes across three repositories in the last two weeks,
including the refund rounding fix that was blocking the finance reconciliation.

Two items need a decision:
1. The React 19 migration has been open nine days and needs a reviewer assigned.
2. Bug intake exceeded closure for the second sprint running — worth a
   conversation about triage capacity.

### Review load

| Reviewer | Reviews given | PRs authored | Pending requests |
|---|---|---|---|
| jchen | 18 | 7 | 4 |
| mpatel | 3 | 9 | 1 |

jchen is doing roughly six times the review work of anyone else on the team.
(Counts of reviews are a workload signal, not a productivity measure.)

## Handling Edge Cases

- **Ambiguous window:** "Last sprint" means nothing without a calendar. Ask for
  dates or state the window you assumed at the top of the report.
- **Relative dates in queries:** GitHub search requires absolute ISO dates.
  Convert first.
- **Private repos:** If searches span an org, some repos may be invisible to
  the user's token. Say the report covers only accessible repositories.
- **Bot noise:** Dependabot and other bots can dominate PR counts. Report them
  separately (`-author:app/dependabot`) rather than inflating human throughput.
- **Search result caps:** GitHub search caps results. When a count is at the
  cap, report it as "1,000+" rather than as an exact figure.
- **Small samples:** Over a short window, a handful of PRs is noise. Say when
  the sample is too small to support a trend claim.
- **No activity:** Zero merged PRs is a real finding. Report it plainly instead
  of padding the report.

## Handling Authentication

If a tool call fails because the user has not connected to GitHub yet:

1. Tell the user: "I need to connect to GitHub to build that report. You should
   see a sign-in prompt — please complete it and I'll continue."
2. Do NOT retry the tool call immediately. Wait for confirmation.
3. If part of the scope is inaccessible, still produce the report but list the
   repositories or teams you could not read. A silently partial report will be
   read as complete.
